using System.Text.Json.Serialization;

namespace Symphony.Operator.Models;

public sealed record HealthDto
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "unknown";

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("running")]
    public int Running { get; init; }

    [JsonPropertyName("retrying")]
    public int Retrying { get; init; }

    [JsonPropertyName("operator_actions_available")]
    public bool OperatorActionsAvailable { get; init; }
}

public sealed record RecentLogsDto
{
    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("lines")]
    public IReadOnlyList<string> Lines { get; init; } = [];
}
