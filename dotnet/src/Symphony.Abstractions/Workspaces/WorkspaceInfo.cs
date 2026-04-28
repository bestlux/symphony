namespace Symphony.Abstractions.Workspaces;

public sealed record WorkspaceInfo(
    string Path,
    string WorkspaceKey,
    bool CreatedNow,
    string? WorkerHost = null,
    string? BaseCommit = null,
    string? BaseBranch = null,
    bool IsClean = false,
    string? Status = null);
