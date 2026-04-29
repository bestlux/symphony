using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Runtime;
using Symphony.Core.Configuration;

namespace Symphony.Core.Orchestration;

public sealed class OrchestratorStateMachine
{
    public const string NoWorkerCapacity = "__no_worker_capacity__";
    public const int ContinuationRetryDelayMs = 1_000;
    public const int FailureRetryBaseMs = 10_000;

    private readonly ConfigResolver _configResolver;
    private readonly WorkerHostSelector _workerHostSelector;

    public OrchestratorStateMachine(
        ConfigResolver? configResolver = null,
        WorkerHostSelector? workerHostSelector = null)
    {
        _configResolver = configResolver ?? new ConfigResolver();
        _workerHostSelector = workerHostSelector ?? new WorkerHostSelector();
    }

    public OrchestratorRuntimeState CreateInitialState(SymphonyConfig config, DateTimeOffset now)
    {
        return new OrchestratorRuntimeState
        {
            PollIntervalMs = config.Polling.IntervalMs,
            MaxConcurrentAgents = config.Agent.MaxConcurrentAgents,
            NextPollDueAt = now,
            PollCheckInProgress = false,
            PollingStatus = new PollingStatus(null, null, null)
        };
    }

    public void RefreshConfig(OrchestratorRuntimeState state, SymphonyConfig config)
    {
        state.PollIntervalMs = config.Polling.IntervalMs;
        state.MaxConcurrentAgents = config.Agent.MaxConcurrentAgents;
    }

    public void BeginPoll(OrchestratorRuntimeState state, DateTimeOffset now)
    {
        state.PollCheckInProgress = true;
        state.NextPollDueAt = null;
        state.PollingStatus = state.PollingStatus with { LastPollStartedAt = now, LastError = null };
    }

    public void CompletePoll(OrchestratorRuntimeState state, SymphonyConfig config, DateTimeOffset now, string? error = null)
    {
        state.PollCheckInProgress = false;
        state.NextPollDueAt = now.AddMilliseconds(config.Polling.IntervalMs);
        state.PollingStatus = state.PollingStatus with
        {
            LastPollCompletedAt = now,
            LastError = error
        };
    }

    public IReadOnlyList<DispatchDecision> ChooseDispatches(
        OrchestratorRuntimeState state,
        SymphonyConfig config,
        IReadOnlyList<Issue> candidates,
        DateTimeOffset now)
    {
        var decisions = new List<DispatchDecision>();
        var activeStates = StateSet(config.Tracker.ActiveStates);
        var dispatchStates = StateSet(config.Tracker.DispatchStates);
        var terminalStates = StateSet(config.Tracker.TerminalStates);

        foreach (var issue in SortIssuesForDispatch(candidates))
        {
            if (!ShouldDispatchIssue(state, config, issue, activeStates, dispatchStates, terminalStates))
            {
                continue;
            }

            var selectedWorker = _workerHostSelector.Select(state, config);
            if (selectedWorker == NoWorkerCapacity)
            {
                break;
            }

            var decision = new DispatchDecision(issue, null, selectedWorker, "candidate");
            decisions.Add(decision);
            MarkRunning(state, decision, now);
        }

        return decisions;
    }

    public DispatchDecision? TryDispatchRetry(
        OrchestratorRuntimeState state,
        SymphonyConfig config,
        Issue issue,
        int attempt,
        string? preferredWorkerHost,
        DateTimeOffset now)
    {
        var activeStates = StateSet(config.Tracker.ActiveStates);
        var dispatchStates = StateSet(config.Tracker.DispatchStates);
        var terminalStates = StateSet(config.Tracker.TerminalStates);

        if (!ShouldDispatchIssue(state, config, issue, activeStates, dispatchStates, terminalStates))
        {
            ReleaseIssueClaim(state, issue.Id);
            return null;
        }

        var selectedWorker = _workerHostSelector.Select(state, config, preferredWorkerHost);
        if (selectedWorker == NoWorkerCapacity)
        {
            return null;
        }

        var decision = new DispatchDecision(issue, attempt, selectedWorker, "retry");
        MarkRunning(state, decision, now);
        return decision;
    }

    public IReadOnlyList<StopDecision> ReconcileRunningIssues(
        OrchestratorRuntimeState state,
        SymphonyConfig config,
        IReadOnlyList<Issue> refreshedIssues)
    {
        var decisions = new List<StopDecision>();
        var activeStates = StateSet(config.Tracker.ActiveStates);
        var terminalStates = StateSet(config.Tracker.TerminalStates);
        var visibleIssues = refreshedIssues.ToDictionary(issue => issue.Id, StringComparer.Ordinal);

        foreach (var running in state.Running.Values.ToArray())
        {
            if (!visibleIssues.TryGetValue(running.IssueId, out var refreshed))
            {
                decisions.Add(StopRunning(state, running.IssueId, false, "issue no longer visible"));
                continue;
            }

            if (IsTerminalState(refreshed.State, terminalStates))
            {
                decisions.Add(StopRunning(state, running.IssueId, true, "issue moved to terminal state"));
                continue;
            }

            if (!IsActiveState(refreshed.State, activeStates) || refreshed.AssignedToWorker == false)
            {
                decisions.Add(StopRunning(state, running.IssueId, false, "issue no longer active for this worker"));
                continue;
            }

            state.Running[running.IssueId] = running with { Issue = refreshed };
        }

        return decisions;
    }

    public IReadOnlyList<StopDecision> RestartStalledIssues(
        OrchestratorRuntimeState state,
        SymphonyConfig config,
        DateTimeOffset now)
    {
        if (config.Codex.StallTimeoutMs <= 0)
        {
            return [];
        }

        var decisions = new List<StopDecision>();
        foreach (var running in state.Running.Values.ToArray())
        {
            var lastActivity = running.LastCodexTimestamp ?? running.StartedAt;
            var elapsedMs = (now - lastActivity).TotalMilliseconds;
            if (elapsedMs <= config.Codex.StallTimeoutMs)
            {
                continue;
            }

            var nextAttempt = (running.RetryAttempt ?? 0) + 1;
            decisions.Add(StopRunning(state, running.IssueId, false, $"stalled for {(long)elapsedMs}ms without codex activity"));
            ScheduleRetry(
                state,
                config,
                running.IssueId,
                running.Identifier,
                nextAttempt,
                now,
                RetryDelayType.Failure,
                $"stalled for {(long)elapsedMs}ms without codex activity",
                running.WorkerHost,
                running.WorkspacePath,
                running.WorkspaceBaseCommit,
                running.WorkspaceBaseBranch,
                running.WorkspaceClean,
                running.WorkspaceStatus);
        }

        return decisions;
    }

    public void MarkCompleted(
        OrchestratorRuntimeState state,
        string issueId,
        bool scheduleContinuationCheck,
        SymphonyConfig config,
        DateTimeOffset now)
    {
        var running = state.Running.TryGetValue(issueId, out var entry) ? entry : null;
        PopRunning(state, issueId);
        state.Completed.Add(issueId);

        if (scheduleContinuationCheck && running is not null)
        {
            ScheduleRetry(
                state,
                config,
                issueId,
                running.Identifier,
                1,
                now,
                RetryDelayType.Continuation,
                null,
                running.WorkerHost,
                running.WorkspacePath,
                running.WorkspaceBaseCommit,
                running.WorkspaceBaseBranch,
                running.WorkspaceClean,
                running.WorkspaceStatus);
        }
    }

    public void MarkCancelled(OrchestratorRuntimeState state, string issueId)
    {
        PopRunning(state, issueId);
    }

    public void MarkFailed(
        OrchestratorRuntimeState state,
        string issueId,
        SymphonyConfig config,
        DateTimeOffset now,
        string? error)
    {
        if (!state.Running.TryGetValue(issueId, out var running))
        {
            return;
        }

        state.Running.Remove(issueId);
        state.CodexTokenUsageByIssue.Remove(issueId);
        ScheduleRetry(
            state,
            config,
            issueId,
            running.Identifier,
            (running.RetryAttempt ?? 0) + 1,
            now,
            RetryDelayType.Failure,
            error,
            running.WorkerHost,
            running.WorkspacePath,
            running.WorkspaceBaseCommit,
            running.WorkspaceBaseBranch,
            running.WorkspaceClean,
            running.WorkspaceStatus);
    }

    public RetryEntry ScheduleRetry(
        OrchestratorRuntimeState state,
        SymphonyConfig config,
        string issueId,
        string identifier,
        int attempt,
        DateTimeOffset now,
        RetryDelayType delayType,
        string? error = null,
        string? workerHost = null,
        string? workspacePath = null,
        string? workspaceBaseCommit = null,
        string? workspaceBaseBranch = null,
        bool workspaceClean = false,
        string? workspaceStatus = null)
    {
        var normalizedAttempt = Math.Max(1, attempt);
        var dueAt = now.AddMilliseconds(RetryDelayMs(normalizedAttempt, delayType, config.Agent.MaxRetryBackoffMs));
        var entry = new RetryEntry(
            issueId,
            identifier,
            normalizedAttempt,
            dueAt,
            error,
            workerHost,
            workspacePath,
            workspaceBaseCommit,
            workspaceBaseBranch,
            workspaceClean,
            workspaceStatus,
            delayType);
        state.RetryAttempts[issueId] = entry;
        state.Claimed.Add(issueId);
        return entry;
    }

    public RetryEntry? PopDueRetry(OrchestratorRuntimeState state, string issueId, DateTimeOffset now)
    {
        if (!state.RetryAttempts.TryGetValue(issueId, out var retry) || retry.DueAt > now)
        {
            return null;
        }

        state.RetryAttempts.Remove(issueId);
        return retry;
    }

    public void IntegrateCodexUpdate(
        OrchestratorRuntimeState state,
        string issueId,
        CodexRuntimeUpdate update)
    {
        if (!state.Running.TryGetValue(issueId, out var running))
        {
            return;
        }

        var usage = update.TokenUsage;
        var inputTokens = running.CodexInputTokens;
        var outputTokens = running.CodexOutputTokens;
        var totalTokens = running.CodexTotalTokens;
        var lastInput = running.LastReportedInputTokens;
        var lastOutput = running.LastReportedOutputTokens;
        var lastTotal = running.LastReportedTotalTokens;

        if (usage is not null)
        {
            var threadKey = TokenUsageThreadKey(update.ThreadId, running.ThreadId);
            if (!state.CodexTokenUsageByIssue.TryGetValue(issueId, out var reportsByThread))
            {
                reportsByThread = new Dictionary<string, CodexTokenUsageTotals>(StringComparer.Ordinal);
                state.CodexTokenUsageByIssue[issueId] = reportsByThread;
            }

            reportsByThread.TryGetValue(threadKey, out var previous);
            var previousInput = previous?.InputTokens ?? 0;
            var previousOutput = previous?.OutputTokens ?? 0;
            var previousTotal = previous?.TotalTokens ?? 0;

            var reportedInput = Math.Max(previousInput, usage.InputTokens);
            var reportedOutput = Math.Max(previousOutput, usage.OutputTokens);
            var reportedTotal = Math.Max(previousTotal, usage.TotalTokens);
            var deltaInput = reportedInput - previousInput;
            var deltaOutput = reportedOutput - previousOutput;
            var deltaTotal = reportedTotal - previousTotal;

            inputTokens += deltaInput;
            outputTokens += deltaOutput;
            totalTokens += deltaTotal;
            lastInput = reportedInput;
            lastOutput = reportedOutput;
            lastTotal = reportedTotal;
            reportsByThread[threadKey] = new CodexTokenUsageTotals(reportedInput, reportedOutput, reportedTotal);

            state.CodexTotals = state.CodexTotals with
            {
                InputTokens = state.CodexTotals.InputTokens + deltaInput,
                OutputTokens = state.CodexTotals.OutputTokens + deltaOutput,
                TotalTokens = state.CodexTotals.TotalTokens + deltaTotal
            };
        }

        if (update.RateLimits is not null)
        {
            state.CodexRateLimits = update.RateLimits;
        }

        var meaningfulActivity = IsMeaningfulActivity(update);
        state.Running[issueId] = running with
        {
            SessionId = update.SessionId ?? ComposeSessionId(update.ThreadId, update.TurnId) ?? running.SessionId,
            ThreadId = update.ThreadId ?? running.ThreadId,
            TurnId = update.TurnId ?? running.TurnId,
            CodexAppServerPid = update.CodexAppServerPid ?? running.CodexAppServerPid,
            LastCodexEvent = meaningfulActivity ? update.Event : running.LastCodexEvent,
            LastCodexTimestamp = meaningfulActivity ? update.Timestamp : running.LastCodexTimestamp,
            LastCodexMessage = meaningfulActivity ? update.Message : running.LastCodexMessage,
            CodexInputTokens = inputTokens,
            CodexOutputTokens = outputTokens,
            CodexTotalTokens = totalTokens,
            LastReportedInputTokens = lastInput,
            LastReportedOutputTokens = lastOutput,
            LastReportedTotalTokens = lastTotal,
            TurnCount = !string.IsNullOrWhiteSpace(update.TurnId) && update.TurnId != running.TurnId
                ? running.TurnCount + 1
                : running.TurnCount
        };
    }

    public RuntimeSnapshot Snapshot(OrchestratorRuntimeState state)
    {
        return new RuntimeSnapshot(
            state.PollIntervalMs,
            state.MaxConcurrentAgents,
            state.NextPollDueAt,
            state.PollCheckInProgress,
            state.Running.Values.Select(ToSnapshot).ToArray(),
            state.RetryAttempts.Values.Select(ToSnapshot).OrderBy(retry => retry.DueAt).ToArray(),
            state.Claimed.Order(StringComparer.Ordinal).ToArray(),
            state.Completed.Order(StringComparer.Ordinal).ToArray(),
            state.CodexTotals,
            state.CodexRateLimits,
            state.PollingStatus);
    }

    public static IReadOnlyList<Issue> SortIssuesForDispatch(IEnumerable<Issue> issues)
    {
        return issues
            .OrderBy(issue => issue.Priority is >= 1 and <= 4 ? issue.Priority.Value : 5)
            .ThenBy(issue => issue.CreatedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(issue => issue.Identifier ?? issue.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private bool ShouldDispatchIssue(
        OrchestratorRuntimeState state,
        SymphonyConfig config,
        Issue issue,
        HashSet<string> activeStates,
        HashSet<string> dispatchStates,
        HashSet<string> terminalStates)
    {
        return CandidateIssue(issue, activeStates, terminalStates)
            && !IsHumanReviewState(issue.State)
            && IsDispatchState(issue.State, dispatchStates)
            && !TodoIssueBlockedByNonTerminal(issue, terminalStates)
            && !state.Claimed.Contains(issue.Id)
            && !state.Running.ContainsKey(issue.Id)
            && AvailableSlots(state) > 0
            && StateSlotsAvailable(state, config, issue)
            && _workerHostSelector.HasAnyCapacity(state, config);
    }

    private void MarkRunning(OrchestratorRuntimeState state, DispatchDecision decision, DateTimeOffset now)
    {
        state.Running[decision.Issue.Id] = new RunningIssue(
            IssueId: decision.Issue.Id,
            Identifier: decision.Issue.Identifier,
            Issue: decision.Issue,
            WorkerHost: decision.WorkerHost,
            WorkspacePath: null,
            WorkspaceBaseCommit: null,
            WorkspaceBaseBranch: null,
            WorkspaceClean: false,
            WorkspaceStatus: null,
            SessionId: null,
            ThreadId: null,
            TurnId: null,
            CodexAppServerPid: null,
            LastCodexEvent: null,
            LastCodexTimestamp: null,
            LastCodexMessage: null,
            CodexInputTokens: 0,
            CodexOutputTokens: 0,
            CodexTotalTokens: 0,
            LastReportedInputTokens: 0,
            LastReportedOutputTokens: 0,
            LastReportedTotalTokens: 0,
            TurnCount: 0,
            RetryAttempt: decision.Attempt,
            StartedAt: now);

        state.CodexTokenUsageByIssue.Remove(decision.Issue.Id);
        state.Claimed.Add(decision.Issue.Id);
        state.RetryAttempts.Remove(decision.Issue.Id);
    }

    private StopDecision StopRunning(
        OrchestratorRuntimeState state,
        string issueId,
        bool cleanupWorkspace,
        string reason)
    {
        var running = state.Running[issueId];
        PopRunning(state, issueId);
        return new StopDecision(issueId, running.Identifier, cleanupWorkspace, reason, running.WorkerHost);
    }

    private static void PopRunning(OrchestratorRuntimeState state, string issueId)
    {
        state.Running.Remove(issueId);
        state.CodexTokenUsageByIssue.Remove(issueId);
        state.Claimed.Remove(issueId);
        state.RetryAttempts.Remove(issueId);
    }

    private static void ReleaseIssueClaim(OrchestratorRuntimeState state, string issueId)
    {
        state.Claimed.Remove(issueId);
        state.RetryAttempts.Remove(issueId);
    }

    private static string TokenUsageThreadKey(string? updateThreadId, string? runningThreadId)
    {
        if (!string.IsNullOrWhiteSpace(updateThreadId))
        {
            return updateThreadId;
        }

        return !string.IsNullOrWhiteSpace(runningThreadId)
            ? runningThreadId
            : "__unknown_thread__";
    }

    private int AvailableSlots(OrchestratorRuntimeState state)
    {
        return Math.Max(0, state.MaxConcurrentAgents - state.Running.Count);
    }

    private bool StateSlotsAvailable(OrchestratorRuntimeState state, SymphonyConfig config, Issue issue)
    {
        var limit = _configResolver.MaxConcurrentAgentsForState(config, issue.State);
        var normalized = ConfigResolver.NormalizeIssueState(issue.State);
        var used = state.Running.Values.Count(running =>
            ConfigResolver.NormalizeIssueState(running.Issue.State) == normalized);

        return used < limit;
    }

    private static bool CandidateIssue(Issue issue, HashSet<string> activeStates, HashSet<string> terminalStates)
    {
        return !string.IsNullOrWhiteSpace(issue.Id)
            && !string.IsNullOrWhiteSpace(issue.Identifier)
            && !string.IsNullOrWhiteSpace(issue.Title)
            && !string.IsNullOrWhiteSpace(issue.State)
            && issue.AssignedToWorker != false
            && IsActiveState(issue.State, activeStates)
            && !IsTerminalState(issue.State, terminalStates);
    }

    private static bool TodoIssueBlockedByNonTerminal(Issue issue, HashSet<string> terminalStates)
    {
        return string.Equals(ConfigResolver.NormalizeIssueState(issue.State), "todo", StringComparison.Ordinal)
            && issue.BlockedBy.Any(blocker =>
                string.IsNullOrWhiteSpace(blocker.State) || !IsTerminalState(blocker.State, terminalStates));
    }

    private static bool IsActiveState(string state, HashSet<string> activeStates)
    {
        return activeStates.Contains(ConfigResolver.NormalizeIssueState(state));
    }

    private static bool IsDispatchState(string state, HashSet<string> dispatchStates)
    {
        return dispatchStates.Count == 0 || dispatchStates.Contains(ConfigResolver.NormalizeIssueState(state));
    }

    private static bool IsHumanReviewState(string state)
    {
        return string.Equals(ConfigResolver.NormalizeIssueState(state), "human review", StringComparison.Ordinal);
    }

    private static bool IsTerminalState(string state, HashSet<string> terminalStates)
    {
        return terminalStates.Contains(ConfigResolver.NormalizeIssueState(state));
    }

    private static HashSet<string> StateSet(IEnumerable<string> states)
    {
        return states
            .Select(ConfigResolver.NormalizeIssueState)
            .Where(state => state.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static int RetryDelayMs(int attempt, RetryDelayType delayType, int maxRetryBackoffMs)
    {
        if (delayType == RetryDelayType.Continuation)
        {
            return ContinuationRetryDelayMs;
        }

        var exponent = Math.Max(0, attempt - 1);
        var delay = FailureRetryBaseMs * Math.Pow(2, exponent);
        return (int)Math.Min(Math.Max(FailureRetryBaseMs, delay), maxRetryBackoffMs);
    }

    private static string? ComposeSessionId(string? threadId, string? turnId)
    {
        return string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId)
            ? null
            : $"{threadId}-{turnId}";
    }

    private static bool IsMeaningfulActivity(CodexRuntimeUpdate update)
    {
        if (update.TokenUsage is not null)
        {
            return true;
        }

        if (string.Equals(update.Event, "stderr", StringComparison.Ordinal)
            && update.Message?.Contains("ignoring interface.defaultPrompt", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        return update.Event is not "account/rateLimits/updated"
            and not "item/started"
            and not "item/completed";
    }

    private static RunningSessionSnapshot ToSnapshot(RunningIssue running)
    {
        return new RunningSessionSnapshot(
            running.IssueId,
            running.Identifier,
            running.Issue,
            running.WorkerHost,
            running.WorkspacePath,
            running.WorkspaceBaseCommit,
            running.WorkspaceBaseBranch,
            running.WorkspaceClean,
            running.WorkspaceStatus,
            running.SessionId,
            running.ThreadId,
            running.TurnId,
            running.CodexAppServerPid,
            running.LastCodexEvent,
            running.LastCodexTimestamp,
            running.LastCodexMessage,
            running.CodexInputTokens,
            running.CodexOutputTokens,
            running.CodexTotalTokens,
            running.LastReportedInputTokens,
            running.LastReportedOutputTokens,
            running.LastReportedTotalTokens,
            running.TurnCount,
            running.RetryAttempt,
            running.StartedAt);
    }

    private static RetryEntrySnapshot ToSnapshot(RetryEntry retry)
    {
        return new RetryEntrySnapshot(
            retry.IssueId,
            retry.Identifier,
            retry.Attempt,
            retry.DueAt,
            retry.Error,
            retry.WorkerHost,
            retry.WorkspacePath,
            retry.WorkspaceBaseCommit,
            retry.WorkspaceBaseBranch,
            retry.WorkspaceClean,
            retry.WorkspaceStatus);
    }
}
