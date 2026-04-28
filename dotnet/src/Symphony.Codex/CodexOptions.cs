namespace Symphony.Codex;

public sealed record CodexOptions
{
    public string Command { get; init; } = "codex app-server";
    public object ApprovalPolicy { get; init; } = "never";
    public string ThreadSandbox { get; init; } = "workspace-write";
    public object TurnSandboxPolicy { get; init; } = new Dictionary<string, object?> { ["mode"] = "workspace-write" };
    public int ReadTimeoutMs { get; init; } = 5_000;
    public int TurnTimeoutMs { get; init; } = 3_600_000;
}

public sealed record CodexTurnRequest(
    string WorkspacePath,
    string Prompt,
    string IssueIdentifier,
    string IssueTitle,
    string? WorkerHost = null,
    CodexOptions? Options = null);

public sealed record CodexTurnResult(
    bool Success,
    string? SessionId,
    string? ThreadId,
    string? TurnId,
    string? Error);

public sealed record CodexRuntimeUpdate(
    string Event,
    DateTimeOffset Timestamp,
    string? SessionId = null,
    string? ThreadId = null,
    string? TurnId = null,
    string? CodexAppServerPid = null,
    string? WorkerHost = null,
    string? LastMessage = null,
    long InputTokens = 0,
    long OutputTokens = 0,
    long TotalTokens = 0,
    object? RateLimits = null,
    object? Payload = null);
