using Symphony.Abstractions.Issues;

namespace Symphony.Abstractions.Runtime;

public sealed record RuntimeSnapshot(
    int PollIntervalMs,
    int MaxConcurrentAgents,
    DateTimeOffset? NextPollDueAt,
    bool PollCheckInProgress,
    IReadOnlyList<RunningSessionSnapshot> Running,
    IReadOnlyList<RetryEntrySnapshot> RetryAttempts,
    IReadOnlyList<string> ClaimedIssueIds,
    IReadOnlyList<string> CompletedIssueIds,
    CodexTotals CodexTotals,
    CodexRateLimitSnapshot? CodexRateLimits,
    PollingStatus PollingStatus);

public sealed record RunningSessionSnapshot(
    string IssueId,
    string Identifier,
    Issue? Issue,
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

public sealed record RetryEntrySnapshot(
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
    string? WorkspaceStatus);

public sealed record CodexTotals(
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    long SecondsRunning);

public sealed record PollingStatus(
    DateTimeOffset? LastPollStartedAt,
    DateTimeOffset? LastPollCompletedAt,
    string? LastError);
