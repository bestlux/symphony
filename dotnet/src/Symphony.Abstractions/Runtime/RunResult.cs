namespace Symphony.Abstractions.Runtime;

public sealed record RunResult(
    string IssueId,
    string IssueIdentifier,
    RunStatus Status,
    string? ThreadId = null,
    string? TurnId = null,
    string? SessionId = null,
    int TurnCount = 0,
    string? Error = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null);

public enum RunStatus
{
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    Stalled
}
