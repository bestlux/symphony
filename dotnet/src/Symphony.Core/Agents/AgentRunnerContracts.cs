using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Runtime;
using Symphony.Abstractions.Workspaces;
using Symphony.Core.Configuration;

namespace Symphony.Core.Agents;

public interface IAgentRunner
{
    Task<RunResult> RunAsync(
        AgentRunRequest request,
        Func<CodexRuntimeUpdate, CancellationToken, Task>? onRuntimeUpdate,
        CancellationToken cancellationToken);
}

public interface IWorkspaceCoordinator
{
    Task<WorkspaceInfo> CreateForIssueAsync(
        Issue issue,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken);

    Task RunBeforeRunHookAsync(
        WorkspaceInfo workspace,
        Issue issue,
        SymphonyConfig config,
        CancellationToken cancellationToken);

    Task RunAfterRunHookAsync(
        WorkspaceInfo workspace,
        Issue issue,
        SymphonyConfig config,
        CancellationToken cancellationToken);

    Task RemoveForIssueAsync(
        Issue issue,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken);
}

public interface ICodexSessionClient
{
    Task<CodexSessionHandle> StartSessionAsync(
        WorkspaceInfo workspace,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken);

    Task<CodexTurnResult> RunTurnAsync(
        CodexSessionHandle session,
        string prompt,
        Issue issue,
        Func<CodexRuntimeUpdate, CancellationToken, Task> onUpdate,
        CancellationToken cancellationToken);

    Task StopSessionAsync(CodexSessionHandle session, CancellationToken cancellationToken);
}

public sealed record AgentRunRequest(
    Issue Issue,
    SymphonyConfig Config,
    string Prompt,
    int? Attempt,
    string? WorkerHost,
    string? ContinueWhileState = null,
    Func<AgentRuntimeInfo, CancellationToken, Task>? OnRuntimeInfo = null);

public sealed record AgentRuntimeInfo(
    string IssueId,
    string? WorkerHost,
    string WorkspacePath,
    string? BaseCommit,
    string? BaseBranch,
    bool IsClean,
    string? Status);

public sealed record CodexSessionHandle(
    string Id,
    WorkspaceInfo Workspace,
    string? WorkerHost,
    SymphonyConfig Config);

public sealed record CodexTurnResult(
    RunStatus Status,
    string? ThreadId,
    string? TurnId,
    string? SessionId,
    string? Error);
