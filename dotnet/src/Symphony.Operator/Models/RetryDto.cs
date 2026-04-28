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
}
