using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Runtime;

namespace Symphony.Core.Orchestration;

public sealed class OrchestratorRuntimeState
{
    public int PollIntervalMs { get; set; }

    public int MaxConcurrentAgents { get; set; }

    public DateTimeOffset? NextPollDueAt { get; set; }

    public bool PollCheckInProgress { get; set; }

    public Dictionary<string, RunningIssue> Running { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Claimed { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, RetryEntry> RetryAttempts { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Completed { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Dictionary<string, CodexTokenUsageTotals>> CodexTokenUsageByIssue { get; } = new(StringComparer.Ordinal);

    public CodexTotals CodexTotals { get; set; } = new(0, 0, 0, 0);

    public CodexRateLimitSnapshot? CodexRateLimits { get; set; }

    public PollingStatus PollingStatus { get; set; } = new(null, null, null);
}

public sealed record RunningIssue(
    string IssueId,
    string Identifier,
    Issue Issue,
    string? WorkerHost,
    string? WorkspacePath,
    string? WorkspaceBaseCommit,
    string? WorkspaceBaseBranch,
    bool WorkspaceClean,
    string? WorkspaceStatus,
    string? SessionId,
    string? ThreadId,
    string? TurnId,
    string? CodexAppServerPid,
    string? LastCodexEvent,
    DateTimeOffset? LastCodexTimestamp,
    string? LastCodexMessage,
    long CodexInputTokens,
    long CodexOutputTokens,
    long CodexTotalTokens,
    long LastReportedInputTokens,
    long LastReportedOutputTokens,
    long LastReportedTotalTokens,
    int TurnCount,
    int? RetryAttempt,
    DateTimeOffset StartedAt);

public sealed record CodexTokenUsageTotals(
    long InputTokens,
    long OutputTokens,
    long TotalTokens);

public sealed record RetryEntry(
    string IssueId,
    string Identifier,
    int Attempt,
    DateTimeOffset DueAt,
    string? Error,
    string? WorkerHost,
    string? WorkspacePath,
    string? WorkspaceBaseCommit,
    string? WorkspaceBaseBranch,
    bool WorkspaceClean,
    string? WorkspaceStatus,
    RetryDelayType DelayType);

public enum RetryDelayType
{
    Failure,
    Continuation
}

public sealed record DispatchDecision(
    Issue Issue,
    int? Attempt,
    string? WorkerHost,
    string Reason);

public sealed record StopDecision(
    string IssueId,
    string Identifier,
    bool CleanupWorkspace,
    string Reason,
    string? WorkerHost = null);
