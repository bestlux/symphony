using Symphony.Abstractions.Runtime;
using Symphony.Abstractions.Tracking;
using Symphony.Core.Configuration;

namespace Symphony.Core.Agents;

public sealed class AgentRunner(
    IWorkspaceCoordinator workspaces,
    ICodexSessionClient codex,
    ITrackerClient tracker)
    : IAgentRunner
{
    public async Task<RunResult> RunAsync(
        AgentRunRequest request,
        Func<CodexRuntimeUpdate, CancellationToken, Task>? onRuntimeUpdate,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var workspace = await workspaces.CreateForIssueAsync(
            request.Issue,
            request.Config,
            request.WorkerHost,
            cancellationToken).ConfigureAwait(false);

        if (request.OnRuntimeInfo is not null)
        {
            await request.OnRuntimeInfo(
                new AgentRuntimeInfo(
                    request.Issue.Id,
                    request.WorkerHost,
                    workspace.Path,
                    workspace.BaseCommit,
                    workspace.BaseBranch,
                    workspace.IsClean,
                    workspace.Status),
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await workspaces.RunBeforeRunHookAsync(
                workspace,
                request.Issue,
                request.Config,
                cancellationToken).ConfigureAwait(false);

            var session = await codex.StartSessionAsync(
                workspace,
                request.Config,
                request.WorkerHost,
                cancellationToken).ConfigureAwait(false);

            try
            {
                return await RunTurnsAsync(
                    session,
                    request,
                    onRuntimeUpdate ?? ((_, _) => Task.CompletedTask),
                    startedAt,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await codex.StopSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(
                request.Issue.Id,
                request.Issue.Identifier,
                RunStatus.Cancelled,
                Error: "cancelled",
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new RunResult(
                request.Issue.Id,
                request.Issue.Identifier,
                RunStatus.Failed,
                Error: ex.Message,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow);
        }
        finally
        {
            try
            {
                await workspaces.RunAfterRunHookAsync(
                    workspace,
                    request.Issue,
                    request.Config,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Elixir parity: after_run is best-effort and must not mask the attempt result.
            }
        }
    }

    private async Task<RunResult> RunTurnsAsync(
        CodexSessionHandle session,
        AgentRunRequest request,
        Func<CodexRuntimeUpdate, CancellationToken, Task> onRuntimeUpdate,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var currentIssue = request.Issue;
        CodexTurnResult? lastTurn = null;

        for (var turnNumber = 1; turnNumber <= request.Config.Agent.MaxTurns; turnNumber++)
        {
            var prompt = turnNumber == 1
                ? request.Prompt
                : BuildContinuationPrompt(turnNumber, request.Config.Agent.MaxTurns);

            lastTurn = await codex.RunTurnAsync(
                session,
                prompt,
                currentIssue,
                onRuntimeUpdate,
                cancellationToken).ConfigureAwait(false);

            if (lastTurn.Status != RunStatus.Succeeded)
            {
                return ToRunResult(request, lastTurn, turnNumber, startedAt);
            }

            var refreshedIssue = await RefreshIssueAsync(currentIssue.Id, cancellationToken).ConfigureAwait(false);
            if (refreshedIssue is null || !ShouldContinue(refreshedIssue, request.Config, request.ContinueWhileState))
            {
                return ToRunResult(request with { Issue = refreshedIssue ?? currentIssue }, lastTurn, turnNumber, startedAt);
            }

            currentIssue = refreshedIssue;
        }

        return ToRunResult(request with { Issue = currentIssue }, lastTurn!, request.Config.Agent.MaxTurns, startedAt);
    }

    private async Task<Symphony.Abstractions.Issues.Issue?> RefreshIssueAsync(
        string issueId,
        CancellationToken cancellationToken)
    {
        var issues = await tracker.FetchIssueStatesByIdsAsync([issueId], cancellationToken).ConfigureAwait(false);
        return issues.FirstOrDefault();
    }

    private static bool ShouldContinue(
        Symphony.Abstractions.Issues.Issue issue,
        SymphonyConfig config,
        string? continueWhileState)
    {
        if (issue.AssignedToWorker == false)
        {
            return false;
        }

        var state = ConfigResolver.NormalizeIssueState(issue.State);
        if (!string.IsNullOrWhiteSpace(continueWhileState))
        {
            return ConfigResolver.NormalizeIssueState(continueWhileState) == state;
        }

        return config.Tracker.ActiveStates.Any(active =>
            ConfigResolver.NormalizeIssueState(active) == state);
    }

    private static RunResult ToRunResult(
        AgentRunRequest request,
        CodexTurnResult turn,
        int turnCount,
        DateTimeOffset startedAt)
    {
        return new RunResult(
            request.Issue.Id,
            request.Issue.Identifier,
            turn.Status,
            turn.ThreadId,
            turn.TurnId,
            turn.SessionId,
            turnCount,
            turn.Error,
            startedAt,
            DateTimeOffset.UtcNow);
    }

    private static string BuildContinuationPrompt(int turnNumber, int maxTurns)
    {
        return $"""
            Continuation guidance:

            - The previous Codex turn completed normally, but the issue is still in an active state.
            - This is continuation turn #{turnNumber} of {maxTurns} for the current agent run.
            - Resume from the current workspace and workpad state instead of restarting from scratch.
            - The original task instructions and prior turn context are already present in this thread.
            - Focus on the remaining ticket work and do not end the turn while the issue stays active unless you are truly blocked.
            """;
    }
}
