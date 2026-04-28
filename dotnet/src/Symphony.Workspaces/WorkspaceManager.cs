using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Workspaces;
using Symphony.Core.Agents;
using Symphony.Core.Configuration;

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

    public async Task<WorkspaceInfo> CreateForIssueAsync(string? issueIdentifier, string? workerHost = null, CancellationToken cancellationToken = default)
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

            return new WorkspaceInfo(workspace, safeId, created, null);
        }

        var remoteWorkspace = PathSafety.RemoteWorkspacePath(options.Root, safeId);
        PathSafety.ValidateRemoteWorkspacePath(remoteWorkspace);
        var remote = await EnsureRemoteWorkspaceAsync(workerHost, remoteWorkspace, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (remote.CreatedNow)
        {
            await _hookRunner.RunRemoteAsync(_sshClient, workerHost, "after_create", options.Hooks.AfterCreate, remote.Path, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
        }

        return new WorkspaceInfo(remote.Path, safeId, remote.CreatedNow, workerHost);
    }

    public Task<WorkspaceInfo> CreateForIssueAsync(
        Issue issue,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken)
    {
        return ForConfig(config).CreateForIssueAsync(issue.Identifier, workerHost, cancellationToken);
    }

    public Task RunBeforeRunHookAsync(
        WorkspaceInfo workspace,
        Issue issue,
        SymphonyConfig config,
        CancellationToken cancellationToken)
    {
        return ForConfig(config).RunBeforeRunHookAsync(workspace, cancellationToken);
    }

    public Task RunAfterRunHookAsync(
        WorkspaceInfo workspace,
        Issue issue,
        SymphonyConfig config,
        CancellationToken cancellationToken)
    {
        return ForConfig(config).RunAfterRunHookBestEffortAsync(workspace, cancellationToken);
    }

    public Task RemoveForIssueAsync(
        Issue issue,
        SymphonyConfig config,
        string? workerHost,
        CancellationToken cancellationToken)
    {
        return ForConfig(config).RemoveIssueWorkspaceAsync(issue.Identifier, workerHost, cancellationToken);
    }

    public async Task RunBeforeRunHookAsync(WorkspaceInfo workspace, CancellationToken cancellationToken = default)
    {
        var options = _optionsFactory();
        if (string.IsNullOrWhiteSpace(workspace.WorkerHost))
        {
            await _hookRunner.RunLocalAsync("before_run", options.Hooks.BeforeRun, workspace.Path, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _hookRunner.RunRemoteAsync(_sshClient, workspace.WorkerHost, "before_run", options.Hooks.BeforeRun, workspace.Path, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RunAfterRunHookBestEffortAsync(WorkspaceInfo workspace, CancellationToken cancellationToken = default)
    {
        var options = _optionsFactory();
        await IgnoreHookFailureAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(workspace.WorkerHost))
            {
                await _hookRunner.RunLocalAsync("after_run", options.Hooks.AfterRun, workspace.Path, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _hookRunner.RunRemoteAsync(_sshClient, workspace.WorkerHost, "after_run", options.Hooks.AfterRun, workspace.Path, options.Hooks.TimeoutMs, cancellationToken).ConfigureAwait(false);
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
