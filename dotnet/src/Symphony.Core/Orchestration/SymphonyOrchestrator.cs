using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Runtime;
using Symphony.Core.Configuration;

namespace Symphony.Core.Orchestration;

public sealed class SymphonyOrchestrator
{
    private readonly object _gate = new();
    private readonly OrchestratorStateMachine _stateMachine;
    private OrchestratorRuntimeState _state;
    private bool _manualRefreshRequested;

    public SymphonyOrchestrator(
        SymphonyConfig initialConfig,
        DateTimeOffset? now = null,
        OrchestratorStateMachine? stateMachine = null)
    {
        _stateMachine = stateMachine ?? new OrchestratorStateMachine();
        _state = _stateMachine.CreateInitialState(initialConfig, now ?? DateTimeOffset.UtcNow);
    }

    public RuntimeSnapshot Snapshot()
    {
        lock (_gate)
        {
            return _stateMachine.Snapshot(_state);
        }
    }

    public void RequestRefresh(DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            _manualRefreshRequested = true;
            _state.NextPollDueAt = now ?? DateTimeOffset.UtcNow;
        }
    }

    public bool ConsumeRefreshRequest()
    {
        lock (_gate)
        {
            var requested = _manualRefreshRequested;
            _manualRefreshRequested = false;
            return requested;
        }
    }

    public IReadOnlyList<WorkspaceCleanupRequest> PlanStartupCleanup(
        SymphonyConfig config,
        IReadOnlyList<Issue> terminalIssues)
    {
        var workerHosts = config.Worker.SshHosts
            .Select(host => host.Trim())
            .Where(host => host.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string?>()
            .Prepend(null)
            .ToArray();

        return terminalIssues
            .SelectMany(issue => workerHosts.Select(workerHost =>
                new WorkspaceCleanupRequest(issue, workerHost)))
            .ToArray();
    }

    public void RefreshConfig(SymphonyConfig config)
    {
        lock (_gate)
        {
            _stateMachine.RefreshConfig(_state, config);
        }
    }

    public void BeginPoll(DateTimeOffset now)
    {
        lock (_gate)
        {
            _stateMachine.BeginPoll(_state, now);
        }
    }

    public void CompletePoll(SymphonyConfig config, DateTimeOffset now, string? error = null)
    {
        lock (_gate)
        {
            _stateMachine.CompletePoll(_state, config, now, error);
        }
    }

    public IReadOnlyList<StopDecision> ReconcileRunningIssues(
        SymphonyConfig config,
        IReadOnlyList<Issue> refreshedIssues)
    {
        lock (_gate)
        {
            return _stateMachine.ReconcileRunningIssues(_state, config, refreshedIssues);
        }
    }

    public IReadOnlyList<StopDecision> RestartStalledIssues(SymphonyConfig config, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _stateMachine.RestartStalledIssues(_state, config, now);
        }
    }

    public IReadOnlyList<DispatchDecision> ChooseDispatches(
        SymphonyConfig config,
        IReadOnlyList<Issue> candidates,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            return _stateMachine.ChooseDispatches(_state, config, candidates, now);
        }
    }

    public DispatchDecision? TryDispatchRetry(
        SymphonyConfig config,
        Issue issue,
        int attempt,
        string? preferredWorkerHost,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            return _stateMachine.TryDispatchRetry(_state, config, issue, attempt, preferredWorkerHost, now);
        }
    }

    public IReadOnlyList<RetryEntry> DueRetries(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _state.RetryAttempts.Values
                .Where(retry => retry.DueAt <= now)
                .OrderBy(retry => retry.DueAt)
                .ToArray();
        }
    }

    public void IntegrateCodexUpdate(string issueId, CodexRuntimeUpdate update)
    {
        lock (_gate)
        {
            _stateMachine.IntegrateCodexUpdate(_state, issueId, update);
        }
    }

    public void IntegrateAgentRuntimeInfo(
        string issueId,
        string? workerHost,
        string workspacePath,
        string? baseCommit,
        string? baseBranch,
        bool isClean,
        string? status)
    {
        lock (_gate)
        {
            if (_state.Running.TryGetValue(issueId, out var running))
            {
                _state.Running[issueId] = running with
                {
                    WorkerHost = workerHost,
                    WorkspacePath = workspacePath,
                    WorkspaceBaseCommit = baseCommit,
                    WorkspaceBaseBranch = baseBranch,
                    WorkspaceClean = isClean,
                    WorkspaceStatus = status
                };
            }
        }
    }

    public void UpdateRunningIssueState(string issueId, string stateName)
    {
        lock (_gate)
        {
            if (_state.Running.TryGetValue(issueId, out var running))
            {
                _state.Running[issueId] = running with
                {
                    Issue = running.Issue with { State = stateName }
                };
            }
        }
    }

    public void MarkCompleted(
        string issueId,
        bool scheduleContinuationCheck,
        SymphonyConfig config,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            _stateMachine.MarkCompleted(_state, issueId, scheduleContinuationCheck, config, now);
        }
    }

    public void MarkCancelled(string issueId)
    {
        lock (_gate)
        {
            _stateMachine.MarkCancelled(_state, issueId);
        }
    }

    public void MarkFailed(
        string issueId,
        SymphonyConfig config,
        DateTimeOffset now,
        string? error)
    {
        lock (_gate)
        {
            _stateMachine.MarkFailed(_state, issueId, config, now, error);
        }
    }
}

public sealed record WorkspaceCleanupRequest(
    Issue Issue,
    string? WorkerHost);
