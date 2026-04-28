using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Workspaces;
using Symphony.Core.Agents;
using Symphony.Core.Configuration;
using System.Diagnostics;

namespace Symphony.Workspaces;

public sealed class WorkspaceManager : IWorkspaceCoordinator
{
    private const string RemoteWorkspaceMarker = "__SYMPHONY_WORKSPACE__";
    private readonly Func<WorkspaceOptions> _optionsFactory;
    private readonly HookRunner _hookRunner;
    private readonly SshClient _sshClient;

    public WorkspaceManager(WorkspaceOptions options)
        : this(() => options, new HookRunner(), new SshClient())
    {
    }

    public WorkspaceManager(IWorkspaceOptionsProvider optionsProvider)
        : this(optionsProvider.GetWorkspaceOptions, new HookRunner(), new SshClient())
    {
    }

    public WorkspaceManager(Func<WorkspaceOptions> optionsFactory, HookRunner hookRunner, SshClient sshClient)
    {
        _optionsFactory = optionsFactory;
        _hookRunner = hookRunner;
        _sshClient = sshClient;
    }

    public async Task<WorkspaceInfo> CreateForIssueAsync(
        string? issueIdentifier,
        string? workerHost = null,
        CancellationToken cancellationToken = default,
        bool requireCleanWorkspace = true)
    {
        var options = _optionsFactory();
        var safeId = PathSafety.SafeIdentifier(issueIdentifier);

        if (string.IsNullOrWhiteSpace(workerHost))
        {
            var workspace = PathSafety.WorkspacePath(options.Root, safeId);
            PathSafety.ValidateLocalWorkspacePath(options.Root, workspace);
            var created = EnsureLocalWorkspace(workspace);

            if (created)
            {
                await _hookRunner.RunLocalAsync("after_create", options.Hooks.AfterCreate, workspace, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
            }

            return RequireCleanIfNeeded(InspectLocalWorkspace(workspace, safeId, created), requireCleanWorkspace);
        }

        var remoteWorkspace = PathSafety.RemoteWorkspacePath(options.Root, safeId);
        PathSafety.ValidateRemoteWorkspacePath(remoteWorkspace);
        var remote = await EnsureRemoteWorkspaceAsync(workerHost, remoteWorkspace, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (remote.CreatedNow)
        {
            await _hookRunner.RunRemoteAsync(_sshClient, workerHost, "after_create", options.Hooks.AfterCreate, remote.Path, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
        }

        return RequireCleanIfNeeded(
            await InspectRemoteWorkspaceAsync(workerHost, remote.Path, safeId, remote.CreatedNow, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false),
            requireCleanWorkspace);
    }

    public Task<WorkspaceInfo> CreateForIssueAsync(
        Issue issue,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken)
    {
        return ForConfig(config).CreateForIssueAsync(
            issue,
            workerHost,
            cancellationToken,
            RequireCleanWorkspaceForIssue(issue));
    }

    public Task RunBeforeRunHookAsync(
        WorkspaceInfo workspace,
        Issue issue,
        SymphonyConfig config,
        CancellationToken cancellationToken)
    {
        return ForConfig(config).RunBeforeRunHookAsync(workspace, issue, cancellationToken);
    }

    public Task RunAfterRunHookAsync(
        WorkspaceInfo workspace,
        Issue issue,
        SymphonyConfig config,
        CancellationToken cancellationToken)
    {
        return ForConfig(config).RunAfterRunHookBestEffortAsync(workspace, issue, cancellationToken);
    }

    private async Task<WorkspaceInfo> CreateForIssueAsync(
        Issue issue,
        string? workerHost = null,
        CancellationToken cancellationToken = default,
        bool requireCleanWorkspace = true)
    {
        var options = _optionsFactory();
        var safeId = PathSafety.SafeIdentifier(issue.Identifier);
        var environment = IssueHookEnvironment(issue);

        if (string.IsNullOrWhiteSpace(workerHost))
        {
            var workspace = PathSafety.WorkspacePath(options.Root, safeId);
            PathSafety.ValidateLocalWorkspacePath(options.Root, workspace);
            var created = EnsureLocalWorkspace(workspace);

            if (created)
            {
                await _hookRunner.RunLocalAsync("after_create", options.Hooks.AfterCreate, workspace, options.Hooks.TimeoutMs, cancellationToken, environment).ConfigureAwait(false);
            }

            return RequireCleanIfNeeded(InspectLocalWorkspace(workspace, safeId, created), requireCleanWorkspace);
        }

        var remoteWorkspace = PathSafety.RemoteWorkspacePath(options.Root, safeId);
        PathSafety.ValidateRemoteWorkspacePath(remoteWorkspace);
        var remote = await EnsureRemoteWorkspaceAsync(workerHost, remoteWorkspace, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (remote.CreatedNow)
        {
            await _hookRunner.RunRemoteAsync(_sshClient, workerHost, "after_create", options.Hooks.AfterCreate, remote.Path, options.Hooks.TimeoutMs, cancellationToken, environment).ConfigureAwait(false);
        }

        return RequireCleanIfNeeded(
            await InspectRemoteWorkspaceAsync(workerHost, remote.Path, safeId, remote.CreatedNow, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false),
            requireCleanWorkspace);
    }

    public Task RemoveForIssueAsync(
        Issue issue,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken)
    {
        return ForConfig(config).RemoveIssueWorkspaceAsync(issue.Identifier, workerHost, cancellationToken);
    }

    public Task RunBeforeRunHookAsync(WorkspaceInfo workspace, CancellationToken cancellationToken = default)
    {
        return RunBeforeRunHookAsync(workspace, issue: null, cancellationToken);
    }

    private async Task RunBeforeRunHookAsync(WorkspaceInfo workspace, Issue? issue, CancellationToken cancellationToken = default)
    {
        var options = _optionsFactory();
        var environment = IssueHookEnvironment(issue);
        if (string.IsNullOrWhiteSpace(workspace.WorkerHost))
        {
            await _hookRunner.RunLocalAsync("before_run", options.Hooks.BeforeRun, workspace.Path, options.Hooks.TimeoutMs, cancellationToken, environment).ConfigureAwait(false);
        }
        else
        {
            await _hookRunner.RunRemoteAsync(_sshClient, workspace.WorkerHost, "before_run", options.Hooks.BeforeRun, workspace.Path, options.Hooks.TimeoutMs, cancellationToken, environment).ConfigureAwait(false);
        }
    }

    public Task RunAfterRunHookBestEffortAsync(WorkspaceInfo workspace, CancellationToken cancellationToken = default)
    {
        return RunAfterRunHookBestEffortAsync(workspace, issue: null, cancellationToken);
    }

    private async Task RunAfterRunHookBestEffortAsync(WorkspaceInfo workspace, Issue? issue, CancellationToken cancellationToken = default)
    {
        var options = _optionsFactory();
        var environment = IssueHookEnvironment(issue);
        await IgnoreHookFailureAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(workspace.WorkerHost))
            {
                await _hookRunner.RunLocalAsync("after_run", options.Hooks.AfterRun, workspace.Path, options.Hooks.TimeoutMs, cancellationToken, environment).ConfigureAwait(false);
            }
            else
            {
                await _hookRunner.RunRemoteAsync(_sshClient, workspace.WorkerHost, "after_run", options.Hooks.AfterRun, workspace.Path, options.Hooks.TimeoutMs, cancellationToken, environment).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public async Task RemoveIssueWorkspaceAsync(string? issueIdentifier, string? workerHost = null, CancellationToken cancellationToken = default)
    {
        var safeId = PathSafety.SafeIdentifier(issueIdentifier);
        var options = _optionsFactory();
        var workspace = string.IsNullOrWhiteSpace(workerHost)
            ? PathSafety.WorkspacePath(options.Root, safeId)
            : PathSafety.RemoteWorkspacePath(options.Root, safeId);
        await RemoveAsync(workspace, workerHost, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string workspace, string? workerHost = null, CancellationToken cancellationToken = default)
    {
        var options = _optionsFactory();
        if (string.IsNullOrWhiteSpace(workerHost))
        {
            if (!Directory.Exists(workspace) && !File.Exists(workspace))
            {
                return;
            }

            PathSafety.ValidateLocalWorkspacePath(options.Root, workspace);
            await IgnoreHookFailureAsync(() => _hookRunner.RunLocalAsync("before_remove", options.Hooks.BeforeRemove, workspace, options.Hooks.TimeoutMs, cancellationToken)).ConfigureAwait(false);

            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
            else if (File.Exists(workspace))
            {
                File.Delete(workspace);
            }

            return;
        }

        PathSafety.ValidateRemoteWorkspacePath(workspace);
        await IgnoreHookFailureAsync(() => _hookRunner.RunRemoteAsync(_sshClient, workerHost, "before_remove", options.Hooks.BeforeRemove, workspace, options.Hooks.TimeoutMs, cancellationToken)).ConfigureAwait(false);

        var result = await _sshClient.RunAsync(workerHost, RemoteAssign("workspace", workspace) + "\nrm -rf \"$workspace\"", options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new WorkspaceException($"Remote workspace removal failed on '{workerHost}' with exit code {result.ExitCode}. Output={result.Output}");
        }
    }

    private static bool EnsureLocalWorkspace(string workspace)
    {
        if (Directory.Exists(workspace))
        {
            return false;
        }

        if (File.Exists(workspace))
        {
            File.Delete(workspace);
        }

        Directory.CreateDirectory(workspace);
        return true;
    }

    private async Task<WorkspaceInfo> EnsureRemoteWorkspaceAsync(string workerHost, string workspace, int timeoutMs, CancellationToken cancellationToken)
    {
        var script = string.Join('\n',
            "set -eu",
            RemoteAssign("workspace", workspace),
            "if [ -d \"$workspace\" ]; then",
            "  created=0",
            "elif [ -e \"$workspace\" ]; then",
            "  rm -rf \"$workspace\"",
            "  mkdir -p \"$workspace\"",
            "  created=1",
            "else",
            "  mkdir -p \"$workspace\"",
            "  created=1",
            "fi",
            "cd \"$workspace\"",
            $"printf '%s\\t%s\\t%s\\n' '{RemoteWorkspaceMarker}' \"$created\" \"$(pwd -P)\"");

        var result = await _sshClient.RunAsync(workerHost, script, timeoutMs, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new WorkspaceException($"Remote workspace preparation failed on '{workerHost}' with exit code {result.ExitCode}. Output={result.Output}");
        }

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length == 3 && parts[0] == RemoteWorkspaceMarker && (parts[1] == "0" || parts[1] == "1") && parts[2].Length > 0)
            {
                return new WorkspaceInfo(parts[2], Path.GetFileName(workspace), parts[1] == "1", workerHost);
            }
        }

        throw new WorkspaceException($"Remote workspace preparation returned unrecognized output. Output={result.Output}");
    }

    private static string RemoteAssign(string variableName, string value)
    {
        return string.Join('\n',
            $"{variableName}={SshClient.ShellEscape(value)}",
            $"case \"${variableName}\" in",
            $"  '~') {variableName}=\"$HOME\" ;;",
            $"  '~/'*) {variableName}=\"$HOME/${{{variableName}#~/}}\" ;;",
            "esac");
    }

    private static WorkspaceInfo InspectLocalWorkspace(string workspace, string workspaceKey, bool created)
    {
        if (!Directory.Exists(Path.Combine(workspace, ".git")))
        {
            throw new WorkspaceException($"Workspace '{workspace}' is not a git repository after preparation.");
        }

        var baseCommit = RunGit(workspace, "rev-parse HEAD").Trim();
        var baseBranch = RunGit(workspace, "rev-parse --abbrev-ref HEAD").Trim();
        var status = RunGit(workspace, "status --porcelain=v1").Trim();
        return new WorkspaceInfo(
            workspace,
            workspaceKey,
            created,
            null,
            string.IsNullOrWhiteSpace(baseCommit) ? null : baseCommit,
            string.IsNullOrWhiteSpace(baseBranch) ? null : baseBranch,
            string.IsNullOrWhiteSpace(status),
            string.IsNullOrWhiteSpace(status) ? null : status);
    }

    private async Task<WorkspaceInfo> InspectRemoteWorkspaceAsync(
        string workerHost,
        string workspace,
        string workspaceKey,
        bool created,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var script = string.Join('\n',
            "set -eu",
            RemoteAssign("workspace", workspace),
            "cd \"$workspace\"",
            "test -d .git",
            "base_commit=$(git rev-parse HEAD)",
            "base_branch=$(git rev-parse --abbrev-ref HEAD)",
            "status=$(git status --porcelain=v1)",
            "if [ -z \"$status\" ]; then clean=1; else clean=0; fi",
            $"printf '%s\\t%s\\t%s\\t%s\\n' '{RemoteWorkspaceMarker}' \"$base_commit\" \"$base_branch\" \"$clean\"",
            "if [ \"$clean\" = 0 ]; then printf '%s\\n' \"$status\"; fi");

        var result = await _sshClient.RunAsync(workerHost, script, timeoutMs, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new WorkspaceException($"Remote workspace git baseline inspection failed on '{workerHost}' with exit code {result.ExitCode}. Output={result.Output}");
        }

        var lines = result.Output.Split('\n', StringSplitOptions.TrimEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split('\t', 4);
            if (parts.Length == 4 && parts[0] == RemoteWorkspaceMarker && (parts[3] == "0" || parts[3] == "1"))
            {
                var isClean = parts[3] == "1";
                var status = isClean
                    ? null
                    : string.Join('\n', lines[(i + 1)..].Where(line => !string.IsNullOrWhiteSpace(line)));
                return new WorkspaceInfo(
                    workspace,
                    workspaceKey,
                    created,
                    workerHost,
                    parts[1],
                    parts[2],
                    isClean,
                    string.IsNullOrWhiteSpace(status) ? null : status);
            }
        }

        throw new WorkspaceException($"Remote workspace git baseline inspection returned unrecognized output. Output={result.Output}");
    }

    private static bool RequireCleanWorkspaceForIssue(Issue issue)
    {
        return string.Equals(issue.State, "Todo", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string?>? IssueHookEnvironment(Issue? issue)
    {
        if (issue is null)
        {
            return null;
        }

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SYMPHONY_ISSUE_ID"] = issue.Id,
            ["SYMPHONY_ISSUE_IDENTIFIER"] = issue.Identifier,
            ["SYMPHONY_ISSUE_BRANCH"] = issue.BranchName,
            ["SYMPHONY_ISSUE_STATE"] = issue.State,
            ["SYMPHONY_BASE_BRANCH"] = "main"
        };
    }

    private static WorkspaceInfo RequireCleanIfNeeded(WorkspaceInfo workspace, bool requireCleanWorkspace)
    {
        return requireCleanWorkspace ? EnsureClean(workspace) : workspace;
    }

    private static WorkspaceInfo EnsureClean(WorkspaceInfo workspace)
    {
        if (workspace.IsClean)
        {
            return workspace;
        }

        throw new WorkspaceException(
            $"Workspace '{workspace.Path}' is dirty before dispatch. BaseCommit={workspace.BaseCommit ?? "<unknown>"} BaseBranch={workspace.BaseBranch ?? "<unknown>"} Status={workspace.Status}");
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new WorkspaceException($"Failed to start git {arguments} in '{workingDirectory}'.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new WorkspaceException($"git {arguments} failed in '{workingDirectory}' with exit code {process.ExitCode}. Output={output} Error={error}");
        }

        return output;
    }

    private static async Task IgnoreHookFailureAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (WorkspaceException)
        {
        }
    }

    private WorkspaceManager ForConfig(SymphonyConfig config) => new(() => FromConfig(config), _hookRunner, _sshClient);

    private static WorkspaceOptions FromConfig(SymphonyConfig config)
    {
        return new WorkspaceOptions
        {
            Root = config.Workspace.Root,
            Hooks = new WorkspaceHooks
            {
                AfterCreate = config.Hooks.AfterCreate,
                BeforeRun = config.Hooks.BeforeRun,
                AfterRun = config.Hooks.AfterRun,
                BeforeRemove = config.Hooks.BeforeRemove,
                TimeoutMs = config.Hooks.TimeoutMs
            }
        };
    }
}
