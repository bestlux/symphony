namespace Symphony.Abstractions.Runtime;

public sealed record CodexRuntimeUpdate(
    string Event,
    DateTimeOffset Timestamp,
    string? ThreadId = null,
    string? TurnId = null,
    string? SessionId = null,
    string? CodexAppServerPid = null,
    string? Message = null,
    CodexTokenUsage? TokenUsage = null,
    CodexRateLimitSnapshot? RateLimits = null);

public sealed record CodexTokenUsage(
    long InputTokens,
    long OutputTokens,
    long TotalTokens);

public sealed record CodexRateLimitSnapshot(
    DateTimeOffset? UpdatedAt,
    IReadOnlyDictionary<string, JsonSerializableValue> Values);

public sealed record JsonSerializableValue(object? Value);
