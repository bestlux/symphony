using System.Text.Json.Serialization;

namespace Symphony.Operator.Models;

public sealed record RetryDto
{
    [JsonPropertyName("issue_id")]
    public string IssueId { get; init; } = "";

    [JsonPropertyName("issue_identifier")]
    public string IssueIdentifier { get; init; } = "";

    [JsonPropertyName("attempt")]
    public int Attempt { get; init; }

    [JsonPropertyName("due_at")]
    public DateTimeOffset DueAt { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

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
}
