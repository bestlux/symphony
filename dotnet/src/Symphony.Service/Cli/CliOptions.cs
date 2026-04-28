namespace Symphony.Service.Cli;

public sealed record CliOptions(
    string WorkflowPath,
    string LogsRoot,
    int? Port,
    string? SecretsPath,
    bool AcknowledgedGuardrails);

public sealed record CliParseResult(bool Success, CliOptions Options, string? Error)
{
    public static CliParseResult Ok(CliOptions options) => new(true, options, null);
    public static CliParseResult Fail(string error) => new(false, new CliOptions("", "", null, null, false), error);
}
