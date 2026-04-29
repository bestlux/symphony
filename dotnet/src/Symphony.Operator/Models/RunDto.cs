using System.Text.Json.Serialization;

namespace Symphony.Operator.Models;

public sealed record RunDto
{
    [JsonPropertyName("issue_id")]
    public string IssueId { get; init; } = "";

    [JsonPropertyName("issue_identifier")]
    public string IssueIdentifier { get; init; } = "";

    [JsonPropertyName("state")]
    public string State { get; init; } = "";

    [JsonPropertyName("worker_host")]
    public string? WorkerHost { get; init; }

    [JsonPropertyName("workspace_path")]
    public string? WorkspacePath { get; init; }

    [JsonPropertyName("workspace_base_commit")]
    public string? WorkspaceBaseCommit { get; init; }

    [JsonPropertyName("workspace_base_branch")]
    public string? WorkspaceBaseBranch { get; init; }

    [JsonPropertyName("workspace_clean")]
    public bool WorkspaceClean { get; init; }

    [JsonPropertyName("workspace_status")]
    public string? WorkspaceStatus { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("thread_id")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turn_id")]
    public string? TurnId { get; init; }

    [JsonPropertyName("codex_app_server_pid")]
    public string? CodexAppServerPid { get; init; }

    [JsonPropertyName("turn_count")]
    public int TurnCount { get; init; }

    [JsonPropertyName("retry_attempt")]
    public int? RetryAttempt { get; init; }

    [JsonPropertyName("last_event")]
    public string? LastEvent { get; init; }

    [JsonPropertyName("last_message")]
    public string? LastMessage { get; init; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("last_event_at")]
    public DateTimeOffset? LastEventAt { get; init; }

    [JsonPropertyName("tokens")]
    public RunTokensDto Tokens { get; init; } = new();
}

public sealed record RunTokensDto
{
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; init; }
}
