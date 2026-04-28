using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Runtime;
using Symphony.Abstractions.Tracking;
using Symphony.Core.Agents;
using Symphony.Core.Configuration;
using Symphony.Core.Orchestration;
using Symphony.Core.Prompts;
using Symphony.Core.Workflow;
using Symphony.Service.Cli;

namespace Symphony.Service.Hosting;

public sealed class SymphonyHostedService : BackgroundService
{
    internal const string TodoState = "Todo";
    internal const string RunningState = "Running";
    internal const string ReadyForReviewState = "Ready for Review";
    internal const string ReviewingState = "Reviewing";
    internal const string BlockedState = "Blocked";
    internal const string DoneState = "Done";

    private readonly CliOptions _options;
    private readonly WorkflowStore _workflowStore;
    private readonly ConfigResolver _configResolver;
    private readonly PromptRenderer _promptRenderer;
    private readonly ITrackerClient _tracker;
    private readonly IAgentRunner _agentRunner;
    private readonly IWorkspaceCoordinator _workspaces;
    private readonly RuntimeStateStore _state;
    private readonly ManualRefreshSignal _refreshSignal;
    private readonly DaemonControlService _controlService;
    private readonly ILogger<SymphonyHostedService> _logger;
    private readonly Dictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);
    private SymphonyOrchestrator? _orchestrator;

    public SymphonyHostedService(
        CliOptions options,
        WorkflowStore workflowStore,
        ConfigResolver configResolver,
        PromptRenderer promptRenderer,
        ITrackerClient tracker,
        IAgentRunner agentRunner,
        IWorkspaceCoordinator workspaces,
        RuntimeStateStore state,
        ManualRefreshSignal refreshSignal,
        DaemonControlService controlService,
        ILogger<SymphonyHostedService> logger)
    {
        _options = options;
        _workflowStore = workflowStore;
        _configResolver = configResolver;
        _promptRenderer = promptRenderer;
        _tracker = tracker;
        _agentRunner = agentRunner;
        _workspaces = workspaces;
        _state = state;
        _refreshSignal = refreshSignal;
        _controlService = controlService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_options.LogsRoot);
        _state.ConfigureCompletedLedger(_options.LogsRoot);

        var config = CurrentConfig();
        _orchestrator = new SymphonyOrchestrator(config);
        _state.SetFromCore(_orchestrator.Snapshot());
        _controlService.Bind(StopRunByIssueIdAsync, RetryRunAsync, RefreshAsync);

        _logger.LogInformation("Symphony service starting workflow={WorkflowPath} logs_root={LogsRoot} port={Port}", _options.WorkflowPath, _options.LogsRoot, _options.Port);

        await RunStartupCleanupAsync(config, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var snapshot = _orchestrator.Snapshot();
            var due = snapshot.NextPollDueAt is null || snapshot.NextPollDueAt <= DateTimeOffset.UtcNow;

            if (_refreshSignal.ConsumeRefresh() || due)
            {
                await RunPollCycleAsync(stoppingToken).ConfigureAwait(false);
            }

            _state.SetFromCore(_orchestrator.Snapshot());
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var cts in _running.Values)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunStartupCleanupAsync(SymphonyConfig config, CancellationToken cancellationToken)
    {
        try
        {
            _configResolver.ValidateForDispatch(config);
            var terminal = await _tracker.FetchIssuesByStatesAsync(config.Tracker.TerminalStates, cancellationToken).ConfigureAwait(false);
            foreach (var cleanup in _orchestrator!.PlanStartupCleanup(config, terminal))
            {
                _logger.LogWarning(
                    "Startup terminal workspace cleanup retained for {IssueIdentifier}; review artifacts must be harvested before cleanup",
                    cleanup.Issue.Identifier);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup terminal workspace cleanup skipped");
        }
    }

    private async Task RunPollCycleAsync(CancellationToken stoppingToken)
    {
        var now = DateTimeOffset.UtcNow;
        var config = CurrentConfig();
        _orchestrator!.RefreshConfig(config);
        _orchestrator.BeginPoll(now);
        _state.SetFromCore(_orchestrator.Snapshot());

        try
        {
            _configResolver.ValidateForDispatch(config);
            await ReconcileAsync(config, stoppingToken).ConfigureAwait(false);
            await DispatchRetriesAsync(config, stoppingToken).ConfigureAwait(false);

            var candidates = await _tracker.FetchCandidateIssuesAsync(stoppingToken).ConfigureAwait(false);
            foreach (var decision in _orchestrator.ChooseDispatches(config, candidates, DateTimeOffset.UtcNow))
            {
                StartAgent(decision, config, attempt: null, stoppingToken);
            }

            _orchestrator.CompletePoll(config, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Poll cycle failed");
            _orchestrator.CompletePoll(config, DateTimeOffset.UtcNow, ex.Message);
        }
        finally
        {
            _state.SetFromCore(_orchestrator.Snapshot());
        }
    }

    private async Task ReconcileAsync(SymphonyConfig config, CancellationToken cancellationToken)
    {
        var runningIds = _orchestrator!.Snapshot().Running.Select(running => running.IssueId).ToArray();
        if (runningIds.Length > 0)
        {
            var refreshed = await _tracker.FetchIssueStatesByIdsAsync(runningIds, cancellationToken).ConfigureAwait(false);
            foreach (var stop in _orchestrator.ReconcileRunningIssues(config, refreshed))
            {
                StopRun(stop, config, cancellationToken);
            }
        }

        foreach (var stop in _orchestrator.RestartStalledIssues(config, DateTimeOffset.UtcNow))
        {
            StopRun(stop, config, cancellationToken);
        }
    }

    private async Task DispatchRetriesAsync(SymphonyConfig config, CancellationToken cancellationToken)
    {
        foreach (var retry in _orchestrator!.DueRetries(DateTimeOffset.UtcNow))
        {
            var issues = await _tracker.FetchIssueStatesByIdsAsync([retry.IssueId], cancellationToken).ConfigureAwait(false);
            var issue = issues.FirstOrDefault();
            if (issue is null)
            {
                continue;
            }

            var decision = _orchestrator.TryDispatchRetry(config, issue, retry.Attempt, retry.WorkerHost, DateTimeOffset.UtcNow);
            if (decision is not null)
            {
                StartAgent(decision, config, retry.Attempt, cancellationToken);
            }
        }
    }

    private void StartAgent(DispatchDecision decision, SymphonyConfig config, int? attempt, CancellationToken parentToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        _running[decision.Issue.Id] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var runKind = RunKind.FromIssueState(decision.Issue.State);
                await MoveClaimedIssueToActiveStateAsync(decision, runKind, cts.Token).ConfigureAwait(false);

                var prompt = BuildRunPrompt(decision.Issue, runKind, attempt);
                var result = await _agentRunner.RunAsync(
                    new AgentRunRequest(
                        decision.Issue,
                        config,
                        prompt,
                        attempt,
                        decision.WorkerHost,
                        runKind.ActiveState,
                        (info, _) =>
                        {
                            _orchestrator!.IntegrateAgentRuntimeInfo(
                                info.IssueId,
                                info.WorkerHost,
                                info.WorkspacePath,
                                info.BaseCommit,
                                info.BaseBranch,
                                info.IsClean,
                                info.Status);
                            _state.SetFromCore(_orchestrator.Snapshot());
                            return Task.CompletedTask;
                        }),
                    (update, _) =>
                    {
                        _orchestrator!.IntegrateCodexUpdate(decision.Issue.Id, update);
                        _state.SetFromCore(_orchestrator.Snapshot());
                        return Task.CompletedTask;
                    },
                    cts.Token).ConfigureAwait(false);

                if (result.Status == RunStatus.Succeeded)
                {
                    _state.RecordCompletion(ToCompletedRunEntry(result, decision.Issue.Id, "retained"));
                    await MoveCompletedIssueAsync(decision, runKind, result, cts.Token).ConfigureAwait(false);
                    _orchestrator!.MarkCompleted(decision.Issue.Id, scheduleContinuationCheck: true, config, DateTimeOffset.UtcNow);
                }
                else if (result.Status != RunStatus.Cancelled)
                {
                    _state.RecordCompletion(ToCompletedRunEntry(result, decision.Issue.Id, "retained"));
                    await MoveIssueStateAsync(decision.Issue, BlockedState, $"Agent run failed: {result.Error ?? "unknown error"}", cts.Token).ConfigureAwait(false);
                    _orchestrator!.MarkFailed(decision.Issue.Id, config, DateTimeOffset.UtcNow, result.Error);
                }
                else
                {
                    if (_orchestrator!.Snapshot().Running.Any(run => run.IssueId == decision.Issue.Id))
                    {
                        _state.RecordCompletion(ToCompletedRunEntry(result, decision.Issue.Id, "retained"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent run failed issue={IssueIdentifier}", decision.Issue.Identifier);
                _state.RecordCompletion(ToCompletedRunEntry(
                    new RunResult(
                        decision.Issue.Id,
                        decision.Issue.Identifier,
                        RunStatus.Failed,
                        Error: ex.Message,
                        StartedAt: DateTimeOffset.UtcNow,
                        CompletedAt: DateTimeOffset.UtcNow),
                    decision.Issue.Id,
                    "retained"));
                _orchestrator!.MarkFailed(decision.Issue.Id, config, DateTimeOffset.UtcNow, ex.Message);
            }
            finally
            {
                _running.Remove(decision.Issue.Id);
                cts.Dispose();
                _state.SetFromCore(_orchestrator!.Snapshot());
            }
        }, CancellationToken.None);
    }

    private string BuildRunPrompt(Issue issue, RunKind runKind, int? attempt)
    {
        var rendered = _promptRenderer.Render(_workflowStore.Current.PromptTemplate, issue, attempt);
        if (!runKind.IsReviewer)
        {
            return rendered;
        }

        return $"""
            You are the neutral Symphony reviewer agent for this issue.

            Your job is to review the implementer work, inspect the workspace/repository state, run reasonable validation, fix review issues when the fix is clearly scoped, and produce a review packet.

            If the work is acceptable after your review and any fixes, say so clearly in your final response.
            If the work is not acceptable and you cannot fix it directly, move the Linear issue to Blocked, explain the blocker, and name the exact next action.

            Review target:
            {rendered}
            """;
    }

    private async Task MoveClaimedIssueToActiveStateAsync(DispatchDecision decision, RunKind runKind, CancellationToken cancellationToken)
    {
        if (!runKind.ShouldMoveOnStart)
        {
            return;
        }

        await MoveIssueStateAsync(
            decision.Issue,
            runKind.ActiveState,
            $"Linear state moved from {decision.Issue.State} to {runKind.ActiveState} after dispatch",
            cancellationToken).ConfigureAwait(false);
        _orchestrator!.UpdateRunningIssueState(decision.Issue.Id, runKind.ActiveState);
        _state.SetFromCore(_orchestrator.Snapshot());
    }

    private async Task MoveCompletedIssueAsync(DispatchDecision decision, RunKind runKind, RunResult result, CancellationToken cancellationToken)
    {
        var latestIssue = await FetchLatestIssueAsync(decision.Issue, cancellationToken).ConfigureAwait(false);
        if (string.Equals(latestIssue.State, BlockedState, StringComparison.OrdinalIgnoreCase))
        {
            _state.RecordIssueStateTransition(latestIssue.Identifier, "Agent left issue Blocked; completion transition skipped");
            return;
        }

        var targetState = runKind.IsReviewer ? DoneState : ReadyForReviewState;
        var message = runKind.IsReviewer
            ? "Reviewer agent approved work and moved issue to Done"
            : "Implementer agent finished work and moved issue to Ready for Review";

        await MoveIssueStateAsync(latestIssue, targetState, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Issue> FetchLatestIssueAsync(Issue fallback, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _tracker.FetchIssueStatesByIdsAsync([fallback.Id], cancellationToken).ConfigureAwait(false);
            return latest.FirstOrDefault() ?? fallback;
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not refresh Linear issue {IssueIdentifier} before completion transition", fallback.Identifier);
            return fallback;
        }
    }

    private async Task MoveIssueStateAsync(Issue issue, string targetState, string message, CancellationToken cancellationToken)
    {
        if (string.Equals(issue.State, targetState, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _tracker.UpdateIssueStateAsync(issue.Id, targetState, cancellationToken).ConfigureAwait(false);
            _state.RecordIssueStateTransition(issue.Identifier, message);
            _logger.LogInformation(
                "Moved Linear issue {IssueIdentifier} from {SourceState} to {TargetState}",
                issue.Identifier,
                issue.State,
                targetState);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failure = $"Linear state move from {issue.State} to {targetState} failed: {ex.Message}";
            _state.RecordIssueStateTransition(issue.Identifier, failure);
            _logger.LogError(
                ex,
                "Failed to move Linear issue {IssueIdentifier} from {SourceState} to {TargetState}",
                issue.Identifier,
                issue.State,
                targetState);
        }
    }

    private void StopRun(StopDecision stop, SymphonyConfig config, CancellationToken cancellationToken)
    {
        var stoppedAt = DateTimeOffset.UtcNow;

        if (_running.Remove(stop.IssueId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        _state.RecordCompletion(ToCompletedRunEntry(
            new RunResult(
                stop.IssueId,
                stop.Identifier,
                RunStatus.Cancelled,
                Error: stop.Reason,
                CompletedAt: stoppedAt),
            stop.IssueId,
            stop.CleanupWorkspace ? "retained" : "not requested"));

        if (!stop.CleanupWorkspace)
        {
            return;
        }

        _logger.LogWarning(
            "Workspace cleanup request retained for {IssueIdentifier}; review artifacts must be harvested before cleanup",
            stop.Identifier);
    }

    private Task RefreshAsync(CancellationToken cancellationToken)
    {
        _refreshSignal.RequestRefresh();
        return Task.CompletedTask;
    }

    private async Task StopRunByIssueIdAsync(string issueId, bool cleanupWorkspace, CancellationToken cancellationToken)
    {
        var snapshot = _orchestrator?.Snapshot();
        var running = snapshot?.Running.FirstOrDefault(run =>
            string.Equals(run.IssueId, issueId, StringComparison.Ordinal)
            || string.Equals(run.Identifier, issueId, StringComparison.OrdinalIgnoreCase));

        if (running is null)
        {
            return;
        }

        if (_running.Remove(running.IssueId, out var cts))
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        _state.RecordCompletion(ToCompletedRunEntry(
            new RunResult(
                running.IssueId,
                running.Identifier,
                RunStatus.Cancelled,
                running.ThreadId,
                running.TurnId,
                running.SessionId,
                running.TurnCount,
                "operator requested stop",
                running.StartedAt,
                DateTimeOffset.UtcNow),
            running.IssueId,
            cleanupWorkspace ? "retained" : "not requested"));
        _orchestrator!.MarkCancelled(running.IssueId);
        if (cleanupWorkspace && running.Issue is not null)
        {
            await _workspaces.RemoveForIssueAsync(
                running.Issue,
                CurrentConfig(),
                running.WorkerHost,
                cancellationToken).ConfigureAwait(false);
        }

        _state.SetFromCore(_orchestrator!.Snapshot());
    }

    private Task RetryRunAsync(string issueId, CancellationToken cancellationToken)
    {
        if (_orchestrator is null)
        {
            return Task.CompletedTask;
        }

        var snapshot = _orchestrator.Snapshot();
        var running = snapshot.Running.FirstOrDefault(run =>
            string.Equals(run.IssueId, issueId, StringComparison.Ordinal)
            || string.Equals(run.Identifier, issueId, StringComparison.OrdinalIgnoreCase));

        if (running is not null)
        {
            if (_running.Remove(running.IssueId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            _orchestrator.MarkFailed(running.IssueId, CurrentConfig(), DateTimeOffset.UtcNow, "operator requested retry");
        }

        _refreshSignal.RequestRefresh();
        _state.SetFromCore(_orchestrator.Snapshot());
        return Task.CompletedTask;
    }

    private SymphonyConfig CurrentConfig() => _configResolver.Resolve(_workflowStore.ReloadIfChanged());

    private CompletedRunEntry ToCompletedRunEntry(RunResult result, string issueId, string cleanupOutcome)
    {
        var running = _orchestrator?.Snapshot().Running.FirstOrDefault(run => run.IssueId == issueId);

        return new CompletedRunEntry(
            result.IssueId,
            result.IssueIdentifier,
            running?.Issue?.State ?? "",
            result.Status.ToString(),
            result.SessionId ?? running?.SessionId,
            result.ThreadId ?? running?.ThreadId,
            result.TurnId ?? running?.TurnId,
            result.TurnCount > 0 ? result.TurnCount : running?.TurnCount ?? 0,
            running?.LastCodexEvent,
            running?.LastCodexMessage,
            result.Error,
            result.StartedAt ?? running?.StartedAt ?? DateTimeOffset.UtcNow,
            result.CompletedAt ?? DateTimeOffset.UtcNow,
            running?.CodexInputTokens ?? 0,
            running?.CodexOutputTokens ?? 0,
            running?.CodexTotalTokens ?? 0,
            running?.WorkerHost,
            running?.WorkspacePath,
            running?.WorkspaceBaseCommit,
            running?.WorkspaceBaseBranch,
            running?.WorkspaceClean ?? false,
            running?.WorkspaceStatus,
            cleanupOutcome);
    }
}

internal sealed record RunKind(bool IsReviewer, string ActiveState)
{
    public bool ShouldMoveOnStart => !string.IsNullOrWhiteSpace(ActiveState);

    public static RunKind FromIssueState(string state)
    {
        if (string.Equals(state, SymphonyHostedService.ReadyForReviewState, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, SymphonyHostedService.ReviewingState, StringComparison.OrdinalIgnoreCase))
        {
            return new RunKind(true, SymphonyHostedService.ReviewingState);
        }

        return new RunKind(false, SymphonyHostedService.RunningState);
    }
}
