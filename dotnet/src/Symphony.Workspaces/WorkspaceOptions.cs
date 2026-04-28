namespace Symphony.Workspaces;

public sealed record WorkspaceOptions
{
    public string Root { get; init; } = Path.Combine(Path.GetTempPath(), "symphony_workspaces");
    public WorkspaceHooks Hooks { get; init; } = new();
}

public sealed record WorkspaceHooks
{
    public string? AfterCreate { get; init; }
    public string? BeforeRun { get; init; }
    public string? AfterRun { get; init; }
    public string? BeforeRemove { get; init; }
    public int TimeoutMs { get; init; } = 60_000;
}

public interface IWorkspaceOptionsProvider
{
    WorkspaceOptions GetWorkspaceOptions();
}
