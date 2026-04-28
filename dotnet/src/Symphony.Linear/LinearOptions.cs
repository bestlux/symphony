namespace Symphony.Linear;

public sealed record LinearOptions
{
    public string Endpoint { get; init; } = "https://api.linear.app/graphql";
    public string? ApiKey { get; init; }
    public string? ProjectSlug { get; init; }
    public IReadOnlyList<string> ActiveStates { get; init; } = ["Todo", "In Progress"];
    public IReadOnlyList<string> TerminalStates { get; init; } = ["Closed", "Cancelled", "Canceled", "Duplicate", "Done"];
    public string? Assignee { get; init; }

    public LinearOptions ResolveEnvironment()
    {
        var apiKey = ResolveValue(ApiKey, "LINEAR_API_KEY");
        var assignee = ResolveValue(Assignee, "LINEAR_ASSIGNEE");

        return this with
        {
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            Assignee = string.IsNullOrWhiteSpace(assignee) ? null : assignee
        };
    }

    private static string? ResolveValue(string? value, string fallbackEnvironmentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Environment.GetEnvironmentVariable(fallbackEnvironmentName);
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('$') && trimmed.Length > 1)
        {
            return Environment.GetEnvironmentVariable(trimmed[1..]);
        }

        return trimmed;
    }
}

public interface ILinearOptionsProvider
{
    LinearOptions GetLinearOptions();
}

