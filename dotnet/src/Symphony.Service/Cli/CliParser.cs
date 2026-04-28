namespace Symphony.Service.Cli;

public static class CliParser
{
    public const string GuardrailsFlag = "--i-understand-that-this-will-be-running-without-the-usual-guardrails";

    public static CliParseResult Parse(string[] args)
    {
        var acknowledgement = false;
        var logsRoot = Path.GetFullPath("log");
        int? port = null;
        string? secretsPath = null;
        var explicitSecretsPath = false;
        string? workflowPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case GuardrailsFlag:
                    acknowledgement = true;
                    break;

                case "--logs-root":
                    if (!TryReadValue(args, ref i, out var logsValue) || string.IsNullOrWhiteSpace(logsValue))
                    {
                        return CliParseResult.Fail(Usage());
                    }

                    logsRoot = Path.GetFullPath(logsValue);
                    break;

                case "--port":
                    if (!TryReadValue(args, ref i, out var portValue) || !int.TryParse(portValue, out var parsedPort) || parsedPort < 0)
                    {
                        return CliParseResult.Fail(Usage());
                    }

                    port = parsedPort;
                    break;

                case "--secrets":
                    if (!TryReadValue(args, ref i, out var secretsValue) || string.IsNullOrWhiteSpace(secretsValue))
                    {
                        return CliParseResult.Fail(Usage());
                    }

                    secretsPath = Path.GetFullPath(secretsValue);
                    explicitSecretsPath = true;
                    break;

                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        return CliParseResult.Fail(Usage());
                    }

                    if (workflowPath is not null)
                    {
                        return CliParseResult.Fail(Usage());
                    }

                    workflowPath = arg;
                    break;
            }
        }

        if (!acknowledgement)
        {
            return CliParseResult.Fail(AcknowledgementBanner());
        }

        workflowPath = Path.GetFullPath(workflowPath ?? "WORKFLOW.md");
        if (!File.Exists(workflowPath))
        {
            return CliParseResult.Fail($"Workflow file not found: {workflowPath}");
        }

        if (explicitSecretsPath && !File.Exists(secretsPath))
        {
            return CliParseResult.Fail($"Secrets file not found: {secretsPath}");
        }

        secretsPath ??= FindDefaultSecretsFile(workflowPath);

        return CliParseResult.Ok(new CliOptions(workflowPath, logsRoot, port, secretsPath, true));
    }

    public static string Usage() =>
        $"Usage: symphony [--logs-root <path>] [--port <port>] [--secrets <path>] [path-to-WORKFLOW.md] {GuardrailsFlag}";

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = "";
            return false;
        }

        value = args[++index];
        return true;
    }

    private static string AcknowledgementBanner() => string.Join(Environment.NewLine, [
        "This Symphony implementation is a low key engineering preview.",
        "Codex will run without the usual guardrails.",
        "Symphony .NET is not a supported product and is presented as-is.",
        $"To proceed, start with `{GuardrailsFlag}` CLI argument"
    ]);

    private static string? FindDefaultSecretsFile(string workflowPath)
    {
        var workflowDirectory = Path.GetDirectoryName(workflowPath) ?? Environment.CurrentDirectory;
        var workflowLocal = Path.Combine(workflowDirectory, "symphony.secrets");
        if (File.Exists(workflowLocal))
        {
            return workflowLocal;
        }

        var currentLocal = Path.GetFullPath("symphony.secrets");
        return File.Exists(currentLocal) ? currentLocal : null;
    }
}
