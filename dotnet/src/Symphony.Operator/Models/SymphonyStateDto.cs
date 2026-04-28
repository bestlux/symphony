using System.Text.Json.Serialization;

namespace Symphony.Operator.Models;

public sealed record SymphonyStateDto
{
    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("counts")]
    public CountsDto Counts { get; init; } = new();

    [JsonPropertyName("running")]
    public IReadOnlyList<RunDto> Running { get; init; } = [];

    [JsonPropertyName("retrying")]
    public IReadOnlyList<RetryDto> Retrying { get; init; } = [];

    [JsonPropertyName("completed")]
    public IReadOnlyList<CompletedRunDto> Completed { get; init; } = [];

    [JsonPropertyName("codex_totals")]
    public CodexTotalsDto CodexTotals { get; init; } = new();
}

public sealed record CountsDto
{
    [JsonPropertyName("running")]
    public int Running { get; init; }

    [JsonPropertyName("retrying")]
    public int Retrying { get; init; }

    [JsonPropertyName("completed")]
    public int Completed { get; init; }
}

public sealed record CodexTotalsDto
{
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; init; }

    [JsonPropertyName("seconds_running")]
    public double SecondsRunning { get; init; }
}
