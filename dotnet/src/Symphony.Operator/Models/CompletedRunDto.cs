using System.Text.Json.Serialization;

namespace Symphony.Operator.Models;

public sealed record CompletedRunDto
{
    [JsonPropertyName("issue_id")]
    public string IssueId { get; init; } = "";

    [JsonPropertyName("issue_identifier")]
    public string IssueIdentifier { get; init; } = "";

    [JsonPropertyName("state")]
    public string State { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

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

    [JsonPropertyName("turn_count")]
    public int TurnCount { get; init; }

    [JsonPropertyName("last_event")]
    public string? LastEvent { get; init; }

    [JsonPropertyName("last_message")]
    public string? LastMessage { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset CompletedAt { get; init; }

    [JsonPropertyName("cleanup_outcome")]
    public string CleanupOutcome { get; init; } = "";

    [JsonPropertyName("tokens")]
    public RunTokensDto Tokens { get; init; } = new();
}
