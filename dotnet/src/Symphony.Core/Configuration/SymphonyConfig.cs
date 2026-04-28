namespace Symphony.Core.Configuration;

public sealed record SymphonyConfig(
    TrackerConfig Tracker,
    PollingConfig Polling,
    WorkspaceConfig Workspace,
    WorkerConfig Worker,
    AgentConfig Agent,
    CodexConfig Codex,
    HooksConfig Hooks,
    ObservabilityConfig Observability,
    ServerConfig Server);

public sealed record TrackerConfig(
    string? Kind,
    string Endpoint,
    string? ApiKey,
    string? ProjectSlug,
    string? Assignee,
    IReadOnlyList<string> ActiveStates,
    IReadOnlyList<string> TerminalStates);

public sealed record PollingConfig(int IntervalMs);

public sealed record WorkspaceConfig(string Root);

public sealed record WorkerConfig(
    IReadOnlyList<string> SshHosts,
    int? MaxConcurrentAgentsPerHost);

public sealed record AgentConfig(
    int MaxConcurrentAgents,
    int MaxTurns,
    int MaxRetryBackoffMs,
    IReadOnlyDictionary<string, int> MaxConcurrentAgentsByState);

public sealed record CodexConfig(
    string Command,
    object ApprovalPolicy,
    string ThreadSandbox,
    IReadOnlyDictionary<string, object?>? TurnSandboxPolicy,
    int TurnTimeoutMs,
    int ReadTimeoutMs,
    int StallTimeoutMs);

public sealed record HooksConfig(
    string? AfterCreate,
    string? BeforeRun,
    string? AfterRun,
    string? BeforeRemove,
    int TimeoutMs);

public sealed record ObservabilityConfig(
    bool DashboardEnabled,
    int RefreshMs,
    int RenderIntervalMs);

public sealed record ServerConfig(
    int? Port,
    string Host);
