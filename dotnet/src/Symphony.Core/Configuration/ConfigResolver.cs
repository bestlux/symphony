using System.Text.RegularExpressions;
using Symphony.Core.Workflow;

namespace Symphony.Core.Configuration;

public sealed partial class ConfigResolver
{
    private static readonly string[] DefaultActiveStates = ["Todo", "In Progress"];
    private static readonly string[] DefaultTerminalStates = ["Closed", "Cancelled", "Canceled", "Duplicate", "Done"];

    private static readonly IReadOnlyDictionary<string, object?> DefaultApprovalPolicy =
        new Dictionary<string, object?>
        {
            ["reject"] = new Dictionary<string, object?>
            {
                ["sandbox_approval"] = true,
                ["rules"] = true,
                ["mcp_elicitations"] = true
            }
        };

    public SymphonyConfig Resolve(WorkflowDefinition workflow)
    {
        var config = workflow.Config;
        var workflowDirectory = Path.GetDirectoryName(workflow.SourcePath) ?? Environment.CurrentDirectory;

        var tracker = Map(config, "tracker");
        var polling = Map(config, "polling");
        var workspace = Map(config, "workspace");
        var worker = Map(config, "worker");
        var agent = Map(config, "agent");
        var codex = Map(config, "codex");
        var hooks = Map(config, "hooks");
        var observability = Map(config, "observability");
        var server = Map(config, "server");

        var defaultWorkspaceRoot = Path.Combine(Path.GetTempPath(), "symphony_workspaces");
        var workspaceRoot = ResolveWorkspaceRoot(
            StringValue(workspace, "root"),
            defaultWorkspaceRoot,
            workflowDirectory);

        return new SymphonyConfig(
            new TrackerConfig(
                StringValue(tracker, "kind"),
                StringValue(tracker, "endpoint") ?? "https://api.linear.app/graphql",
                ResolveSecret(StringValue(tracker, "api_key"), "LINEAR_API_KEY"),
                StringValue(tracker, "project_slug"),
                ResolveSecret(StringValue(tracker, "assignee"), "LINEAR_ASSIGNEE"),
                StringList(tracker, "active_states", DefaultActiveStates),
                StringList(tracker, "terminal_states", DefaultTerminalStates)),
            new PollingConfig(PositiveInt(polling, "interval_ms", 30_000)),
            new WorkspaceConfig(workspaceRoot),
            new WorkerConfig(
                StringList(worker, "ssh_hosts", Array.Empty<string>()),
                OptionalPositiveInt(worker, "max_concurrent_agents_per_host")),
            new AgentConfig(
                PositiveInt(agent, "max_concurrent_agents", 10),
                PositiveInt(agent, "max_turns", 20),
                PositiveInt(agent, "max_retry_backoff_ms", 300_000),
                StateLimits(Map(agent, "max_concurrent_agents_by_state"))),
            new CodexConfig(
                StringValue(codex, "command") ?? "codex app-server",
                ObjectValue(codex, "approval_policy") ?? DefaultApprovalPolicy,
                StringValue(codex, "thread_sandbox") ?? "workspace-write",
                MapOrNull(codex, "turn_sandbox_policy"),
                PositiveInt(codex, "turn_timeout_ms", 3_600_000),
                PositiveInt(codex, "read_timeout_ms", 5_000),
                NonNegativeInt(codex, "stall_timeout_ms", 300_000)),
            new HooksConfig(
                StringValue(hooks, "after_create"),
                StringValue(hooks, "before_run"),
                StringValue(hooks, "after_run"),
                StringValue(hooks, "before_remove"),
                PositiveInt(hooks, "timeout_ms", 60_000)),
            new ObservabilityConfig(
                BoolValue(observability, "dashboard_enabled", true),
                PositiveInt(observability, "refresh_ms", 1_000),
                PositiveInt(observability, "render_interval_ms", 16)),
            new ServerConfig(
                OptionalNonNegativeInt(server, "port"),
                StringValue(server, "host") ?? "127.0.0.1"));
    }

    public void ValidateForDispatch(SymphonyConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Tracker.Kind))
        {
            throw new ConfigValidationException("Tracker kind missing in WORKFLOW.md.", "missing_tracker_kind");
        }

        if (!string.Equals(config.Tracker.Kind, "linear", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(config.Tracker.Kind, "memory", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigValidationException($"Unsupported tracker kind in WORKFLOW.md: {config.Tracker.Kind}", "unsupported_tracker_kind");
        }

        if (string.Equals(config.Tracker.Kind, "linear", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(config.Tracker.ApiKey))
        {
            throw new ConfigValidationException("Linear API token missing in WORKFLOW.md.", "missing_linear_api_token");
        }

        if (string.Equals(config.Tracker.Kind, "linear", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(config.Tracker.ProjectSlug))
        {
            throw new ConfigValidationException("Linear project slug missing in WORKFLOW.md.", "missing_linear_project_slug");
        }
    }

    public int MaxConcurrentAgentsForState(SymphonyConfig config, string? stateName)
    {
        if (stateName is not null
            && config.Agent.MaxConcurrentAgentsByState.TryGetValue(NormalizeIssueState(stateName), out var limit))
        {
            return limit;
        }

        return config.Agent.MaxConcurrentAgents;
    }

    public IReadOnlyDictionary<string, object?> ResolveTurnSandboxPolicy(SymphonyConfig config, string? workspace = null)
    {
        if (config.Codex.TurnSandboxPolicy is not null)
        {
            return config.Codex.TurnSandboxPolicy;
        }

        var root = string.IsNullOrWhiteSpace(workspace) ? config.Workspace.Root : workspace;
        return new Dictionary<string, object?>
        {
            ["type"] = "workspaceWrite",
            ["writableRoots"] = new[] { Path.GetFullPath(root!) },
            ["readOnlyAccess"] = new Dictionary<string, object?> { ["type"] = "fullAccess" },
            ["networkAccess"] = false,
            ["excludeTmpdirEnvVar"] = false,
            ["excludeSlashTmp"] = false
        };
    }

    public static string NormalizeIssueState(string stateName) => stateName.Trim().ToLowerInvariant();

    private static IReadOnlyDictionary<string, object?> Map(IReadOnlyDictionary<string, object?> source, string key)
    {
        return source.TryGetValue(key, out var value) && value is IReadOnlyDictionary<string, object?> map
            ? map
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, object?>? MapOrNull(IReadOnlyDictionary<string, object?> source, string key)
    {
        return source.TryGetValue(key, out var value) && value is IReadOnlyDictionary<string, object?> map
            ? map
            : null;
    }

    private static string? StringValue(IReadOnlyDictionary<string, object?> source, string key)
    {
        return source.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static object? ObjectValue(IReadOnlyDictionary<string, object?> source, string key)
    {
        return source.TryGetValue(key, out var value) ? value : null;
    }

    private static bool BoolValue(IReadOnlyDictionary<string, object?> source, string key, bool fallback)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value is bool boolean ? boolean : bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static int PositiveInt(IReadOnlyDictionary<string, object?> source, string key, int fallback)
    {
        var value = IntValue(source, key) ?? fallback;
        if (value <= 0)
        {
            throw new ConfigValidationException($"{key} must be greater than 0.", "invalid_workflow_config");
        }

        return value;
    }

    private static int NonNegativeInt(IReadOnlyDictionary<string, object?> source, string key, int fallback)
    {
        var value = IntValue(source, key) ?? fallback;
        if (value < 0)
        {
            throw new ConfigValidationException($"{key} must be greater than or equal to 0.", "invalid_workflow_config");
        }

        return value;
    }

    private static int? OptionalPositiveInt(IReadOnlyDictionary<string, object?> source, string key)
    {
        var value = IntValue(source, key);
        if (value is null)
        {
            return null;
        }

        if (value <= 0)
        {
            throw new ConfigValidationException($"{key} must be greater than 0.", "invalid_workflow_config");
        }

        return value;
    }

    private static int? OptionalNonNegativeInt(IReadOnlyDictionary<string, object?> source, string key)
    {
        var value = IntValue(source, key);
        if (value is null)
        {
            return null;
        }

        if (value < 0)
        {
            throw new ConfigValidationException($"{key} must be greater than or equal to 0.", "invalid_workflow_config");
        }

        return value;
    }

    private static int? IntValue(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int integer => integer,
            long integer => checked((int)integer),
            _ when int.TryParse(value.ToString(), out var parsed) => parsed,
            _ => throw new ConfigValidationException($"{key} must be an integer.", "invalid_workflow_config")
        };
    }

    private static IReadOnlyList<string> StringList(
        IReadOnlyDictionary<string, object?> source,
        string key,
        IReadOnlyList<string> fallback)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        if (value is not string && value is IEnumerable<object?> list)
        {
            return list.Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }

        return [value.ToString()!];
    }

    private static IReadOnlyDictionary<string, int> StateLimits(IReadOnlyDictionary<string, object?> source)
    {
        return source
            .Select(pair => (State: NormalizeIssueState(pair.Key), Limit: CoerceOptionalPositiveInt(pair.Value)))
            .Where(item => !string.IsNullOrWhiteSpace(item.State) && item.Limit is not null)
            .ToDictionary(item => item.State, item => item.Limit!.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static int? CoerceOptionalPositiveInt(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var parsed = value is int integer ? integer : int.TryParse(value.ToString(), out var result) ? result : 0;
        return parsed > 0 ? parsed : null;
    }

    private static string? ResolveSecret(string? value, string fallbackEnvironmentVariable)
    {
        var resolved = string.IsNullOrWhiteSpace(value)
            ? Environment.GetEnvironmentVariable(fallbackEnvironmentVariable)
            : ResolveSingleEnvironmentReference(value, Environment.GetEnvironmentVariable(fallbackEnvironmentVariable));

        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

    private static string ResolveWorkspaceRoot(string? value, string defaultValue, string workflowDirectory)
    {
        var expanded = ExpandPath(string.IsNullOrWhiteSpace(value) ? defaultValue : value);
        if (string.IsNullOrWhiteSpace(expanded))
        {
            expanded = defaultValue;
        }

        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(workflowDirectory, expanded);
        }

        return Path.GetFullPath(expanded);
    }

    private static string ExpandPath(string value)
    {
        var expanded = ResolveSingleEnvironmentReference(value, string.Empty) ?? value;

        if (expanded.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = expanded.Length == 1
                ? home
                : Path.Combine(home, expanded[2..]);
        }

        expanded = EnvironmentVariablePattern().Replace(expanded, match =>
            Environment.GetEnvironmentVariable(match.Groups["name"].Value) ?? string.Empty);

        return Environment.ExpandEnvironmentVariables(expanded);
    }

    private static string? ResolveSingleEnvironmentReference(string value, string? fallback)
    {
        if (!value.StartsWith('$') || !EnvironmentNamePattern().IsMatch(value[1..]))
        {
            return value;
        }

        var resolved = Environment.GetEnvironmentVariable(value[1..]);
        return string.IsNullOrEmpty(resolved) ? fallback : resolved;
    }

    [GeneratedRegex(@"\$(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex EnvironmentVariablePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvironmentNamePattern();
}

public sealed class ConfigValidationException : InvalidOperationException
{
    public ConfigValidationException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
