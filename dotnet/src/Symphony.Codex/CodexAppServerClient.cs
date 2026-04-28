using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Workspaces;
using Symphony.Core.Agents;
using Symphony.Core.Configuration;
using AbstractionsUpdate = Symphony.Abstractions.Runtime.CodexRuntimeUpdate;
using CoreCodexTurnResult = Symphony.Core.Agents.CodexTurnResult;

namespace Symphony.Codex;

public sealed class CodexAppServerClient : ICodexSessionClient
{
    private readonly DynamicToolDispatcher _toolDispatcher;
    private readonly ConcurrentDictionary<string, ActiveSession> _sessions = new(StringComparer.Ordinal);

    public CodexAppServerClient(DynamicToolDispatcher? toolDispatcher = null)
    {
        _toolDispatcher = toolDispatcher ?? new DynamicToolDispatcher();
    }

    public Task<CodexSessionHandle> StartSessionAsync(
        WorkspaceInfo workspace,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken)
    {
        return StartSessionCoreAsync(workspace, config, workerHost, cancellationToken);
    }

    public async Task<CoreCodexTurnResult> RunTurnAsync(
        CodexSessionHandle session,
        string prompt,
        Issue issue,
        Func<AbstractionsUpdate, CancellationToken, Task> onUpdate,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(session.Id, out var active))
        {
            return new CoreCodexTurnResult(
                Symphony.Abstractions.Runtime.RunStatus.Failed,
                null,
                null,
                null,
                "Codex session is not active.");
        }

        active.StartErrorDrain(update => onUpdate(ToAbstractionsUpdate(update), cancellationToken), cancellationToken);
        var result = await RunTurnOnActiveSessionAsync(
            active,
            prompt,
            issue,
            update => onUpdate(ToAbstractionsUpdate(update), cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return new CoreCodexTurnResult(
            result.Success ? Symphony.Abstractions.Runtime.RunStatus.Succeeded : Symphony.Abstractions.Runtime.RunStatus.Failed,
            result.ThreadId,
            result.TurnId,
            result.SessionId,
            result.Error);
    }

    public async Task StopSessionAsync(CodexSessionHandle session, CancellationToken cancellationToken)
    {
        if (_sessions.TryRemove(session.Id, out var active))
        {
            await active.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static CodexOptions FromConfig(SymphonyConfig config, string workspacePath)
    {
        var resolver = new ConfigResolver();
        return new CodexOptions
        {
            Command = config.Codex.Command,
            ApprovalPolicy = config.Codex.ApprovalPolicy is string text ? text : config.Codex.ApprovalPolicy,
            ThreadSandbox = config.Codex.ThreadSandbox,
            TurnSandboxPolicy = config.Codex.TurnSandboxPolicy ?? resolver.ResolveTurnSandboxPolicy(config, workspacePath),
            ReadTimeoutMs = config.Codex.ReadTimeoutMs,
            TurnTimeoutMs = config.Codex.TurnTimeoutMs
        };
    }

    private static AbstractionsUpdate ToAbstractionsUpdate(CodexRuntimeUpdate update)
    {
        return new AbstractionsUpdate(
            update.Event,
            update.Timestamp,
            update.ThreadId,
            update.TurnId,
            update.SessionId,
            update.CodexAppServerPid,
            update.LastMessage,
            update.TotalTokens > 0
                ? new Symphony.Abstractions.Runtime.CodexTokenUsage(update.InputTokens, update.OutputTokens, update.TotalTokens)
                : null,
            null);
    }

    private async Task<CodexSessionHandle> StartSessionCoreAsync(
        WorkspaceInfo workspace,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken)
    {
        var options = FromConfig(config, workspace.Path);
        var process = CodexProcess.Start(workspace.Path, options.Command, workerHost);

        try
        {
            await SendAsync(process, CodexProtocol.Initialize(), cancellationToken).ConfigureAwait(false);
            var init = await AwaitResponseAsync(process, CodexProtocol.InitializeId, options.ReadTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (init.Error is not null)
            {
                throw new InvalidOperationException(init.Error);
            }

            await SendAsync(process, CodexProtocol.Initialized(), cancellationToken).ConfigureAwait(false);

            await SendAsync(process, CodexProtocol.ThreadStart(workspace.Path, options), cancellationToken).ConfigureAwait(false);
            var thread = await AwaitResponseAsync(process, CodexProtocol.ThreadStartId, options.ReadTimeoutMs, cancellationToken).ConfigureAwait(false);
            var threadId = thread.Result?["thread"]?["id"]?.GetValue<string>();
            if (thread.Error is not null || string.IsNullOrWhiteSpace(threadId))
            {
                throw new InvalidOperationException(thread.Error ?? "Invalid thread/start response.");
            }

            var active = new ActiveSession(threadId, process, workspace, workerHost, options);
            _sessions[threadId] = active;
            return new CodexSessionHandle(threadId, workspace, workerHost, config);
        }
        catch
        {
            await process.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<CodexTurnResult> RunTurnOnActiveSessionAsync(
        ActiveSession active,
        string prompt,
        Issue issue,
        Func<CodexRuntimeUpdate, Task>? onUpdate,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            active.Process,
            CodexProtocol.TurnStart(
                active.ThreadId,
                prompt,
                active.Workspace.Path,
                $"{issue.Identifier}: {issue.Title}",
                active.Options),
            cancellationToken).ConfigureAwait(false);

        var turn = await AwaitResponseAsync(active.Process, CodexProtocol.TurnStartId, active.Options.ReadTimeoutMs, cancellationToken).ConfigureAwait(false);
        var turnId = turn.Result?["turn"]?["id"]?.GetValue<string>();
        if (turn.Error is not null || string.IsNullOrWhiteSpace(turnId))
        {
            return await FailAsync("startup_failed", turn.Error ?? "Invalid turn/start response.", onUpdate, active.Process, active.WorkerHost, active.ThreadId).ConfigureAwait(false);
        }

        var sessionId = $"{active.ThreadId}-{turnId}";
        await EmitAsync(onUpdate, CodexEventMapper.Map("session_started", null, null, active.Process.ProcessId, active.WorkerHost, sessionId, active.ThreadId, turnId)).ConfigureAwait(false);

        using var turnTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turnTimeout.CancelAfter(active.Options.TurnTimeoutMs);

        var completion = await ReceiveUntilTurnEndsAsync(active.Process, active.Options, onUpdate, active.WorkerHost, sessionId, active.ThreadId, turnId, turnTimeout.Token).ConfigureAwait(false);
        return completion
            ? new CodexTurnResult(true, sessionId, active.ThreadId, turnId, null)
            : new CodexTurnResult(false, sessionId, active.ThreadId, turnId, "Codex turn ended with failure.");
    }

    public async Task<CodexTurnResult> RunAsync(
        CodexTurnRequest request,
        Func<CodexRuntimeUpdate, Task>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        var options = request.Options ?? new CodexOptions();
        await using var process = CodexProcess.Start(request.WorkspacePath, options.Command, request.WorkerHost);
        _ = DrainErrorsAsync(process, onUpdate, request.WorkerHost, cancellationToken);

        try
        {
            await SendAsync(process, CodexProtocol.Initialize(), cancellationToken);
            var init = await AwaitResponseAsync(process, CodexProtocol.InitializeId, options.ReadTimeoutMs, cancellationToken);
            if (init.Error is not null)
            {
                return await FailAsync("startup_failed", init.Error, onUpdate, process, request.WorkerHost);
            }

            await SendAsync(process, CodexProtocol.Initialized(), cancellationToken);

            await SendAsync(process, CodexProtocol.ThreadStart(request.WorkspacePath, options), cancellationToken);
            var thread = await AwaitResponseAsync(process, CodexProtocol.ThreadStartId, options.ReadTimeoutMs, cancellationToken);
            var threadId = thread.Result?["thread"]?["id"]?.GetValue<string>();
            if (thread.Error is not null || string.IsNullOrWhiteSpace(threadId))
            {
                return await FailAsync("startup_failed", thread.Error ?? "Invalid thread/start response.", onUpdate, process, request.WorkerHost);
            }

            await SendAsync(process, CodexProtocol.TurnStart(threadId, request.Prompt, request.WorkspacePath, $"{request.IssueIdentifier}: {request.IssueTitle}", options), cancellationToken);
            var turn = await AwaitResponseAsync(process, CodexProtocol.TurnStartId, options.ReadTimeoutMs, cancellationToken);
            var turnId = turn.Result?["turn"]?["id"]?.GetValue<string>();
            if (turn.Error is not null || string.IsNullOrWhiteSpace(turnId))
            {
                return await FailAsync("startup_failed", turn.Error ?? "Invalid turn/start response.", onUpdate, process, request.WorkerHost, threadId);
            }

            var sessionId = $"{threadId}-{turnId}";
            await EmitAsync(onUpdate, CodexEventMapper.Map("session_started", null, null, process.ProcessId, request.WorkerHost, sessionId, threadId, turnId));

            using var turnTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            turnTimeout.CancelAfter(options.TurnTimeoutMs);

            var completion = await ReceiveUntilTurnEndsAsync(process, options, onUpdate, request.WorkerHost, sessionId, threadId, turnId, turnTimeout.Token);
            return completion
                ? new CodexTurnResult(true, sessionId, threadId, turnId, null)
                : new CodexTurnResult(false, sessionId, threadId, turnId, "Codex turn ended with failure.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailAsync("turn_timeout", "Codex turn timed out.", onUpdate, process, request.WorkerHost);
        }
        catch (Exception ex)
        {
            return await FailAsync("turn_ended_with_error", ex.Message, onUpdate, process, request.WorkerHost);
        }
    }

    private async Task<bool> ReceiveUntilTurnEndsAsync(
        CodexProcess process,
        CodexOptions options,
        Func<CodexRuntimeUpdate, Task>? onUpdate,
        string? workerHost,
        string sessionId,
        string threadId,
        string turnId,
        CancellationToken cancellationToken)
    {
        var handler = new ApprovalHandler(_toolDispatcher);
        var autoApprove = options.ApprovalPolicy is string policy
            && string.Equals(policy, "never", StringComparison.OrdinalIgnoreCase);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                await EmitAsync(onUpdate, CodexEventMapper.Map("turn_failed", null, "Codex app-server exited.", process.ProcessId, workerHost, sessionId, threadId, turnId));
                return false;
            }

            if (!TryParseObject(line, out var payload))
            {
                await EmitAsync(onUpdate, CodexEventMapper.Map("malformed", null, line, process.ProcessId, workerHost, sessionId, threadId, turnId));
                continue;
            }

            var method = payload["method"]?.GetValue<string>();
            if (method == "turn/completed")
            {
                await EmitAsync(onUpdate, CodexEventMapper.Map("turn_completed", payload, line, process.ProcessId, workerHost, sessionId, threadId, turnId));
                return true;
            }

            if (method is "turn/failed" or "turn/cancelled")
            {
                await EmitAsync(onUpdate, CodexEventMapper.Map(method == "turn/failed" ? "turn_failed" : "turn_cancelled", payload, line, process.ProcessId, workerHost, sessionId, threadId, turnId));
                return false;
            }

            var handled = await handler.TryHandleAsync(payload, message => SendAsync(process, message, cancellationToken), autoApprove, cancellationToken);
            if (handled == ApprovalHandlingResult.RequiresApproval)
            {
                await EmitAsync(onUpdate, CodexEventMapper.Map("approval_required", payload, line, process.ProcessId, workerHost, sessionId, threadId, turnId));
                return false;
            }

            if (handled == ApprovalHandlingResult.RequiresInput || ApprovalHandler.NeedsInput(method, payload))
            {
                await EmitAsync(onUpdate, CodexEventMapper.Map("turn_input_required", payload, line, process.ProcessId, workerHost, sessionId, threadId, turnId));
                return false;
            }

            var eventName = handled == ApprovalHandlingResult.Replied ? "approval_auto_approved" : "notification";
            await EmitAsync(onUpdate, CodexEventMapper.Map(eventName, payload, line, process.ProcessId, workerHost, sessionId, threadId, turnId));
        }

        return false;
    }

    private static async Task SendAsync(CodexProcess process, JsonObject message, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(message.ToJsonString(JsonDefaults.Options).AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<(JsonObject? Result, string? Error)> AwaitResponseAsync(
        CodexProcess process,
        int requestId,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        while (!timeout.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                return (null, "Codex app-server exited while waiting for response.");
            }

            if (!TryParseObject(line, out var payload))
            {
                continue;
            }

            if (payload["id"]?.GetValue<int?>() != requestId)
            {
                continue;
            }

            if (payload["error"] is JsonNode error)
            {
                return (null, error.ToJsonString(JsonDefaults.Options));
            }

            return (payload["result"] as JsonObject, null);
        }

        return (null, "Timed out waiting for Codex app-server response.");
    }

    private static bool TryParseObject(string line, out JsonObject payload)
    {
        try
        {
            payload = JsonNode.Parse(line) as JsonObject ?? new JsonObject();
            return payload.Count > 0;
        }
        catch (JsonException)
        {
            payload = new JsonObject();
            return false;
        }
    }

    private static async Task<CodexTurnResult> FailAsync(
        string eventName,
        string error,
        Func<CodexRuntimeUpdate, Task>? onUpdate,
        CodexProcess process,
        string? workerHost,
        string? threadId = null)
    {
        await EmitAsync(onUpdate, CodexEventMapper.Map(eventName, null, error, process.ProcessId, workerHost, threadId: threadId));
        return new CodexTurnResult(false, null, threadId, null, error);
    }

    private static async Task EmitAsync(Func<CodexRuntimeUpdate, Task>? onUpdate, CodexRuntimeUpdate update)
    {
        if (onUpdate is not null)
        {
            await onUpdate(update);
        }
    }

    private static async Task DrainErrorsAsync(
        CodexProcess process,
        Func<CodexRuntimeUpdate, Task>? onUpdate,
        string? workerHost,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    return;
                }

                await EmitAsync(onUpdate, CodexEventMapper.Map("stderr", null, line, process.ProcessId, workerHost));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class ActiveSession(
        string threadId,
        CodexProcess process,
        WorkspaceInfo workspace,
        string? workerHost,
        CodexOptions options)
        : IAsyncDisposable
    {
        private int _errorDrainStarted;

        public string ThreadId { get; } = threadId;
        public CodexProcess Process { get; } = process;
        public WorkspaceInfo Workspace { get; } = workspace;
        public string? WorkerHost { get; } = workerHost;
        public CodexOptions Options { get; } = options;

        public void StartErrorDrain(Func<CodexRuntimeUpdate, Task>? onUpdate, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _errorDrainStarted, 1) == 0)
            {
                _ = DrainErrorsAsync(Process, onUpdate, WorkerHost, cancellationToken);
            }
        }

        public ValueTask DisposeAsync() => Process.DisposeAsync();
    }
}
