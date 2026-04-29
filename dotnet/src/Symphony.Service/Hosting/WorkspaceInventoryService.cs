using System.Diagnostics;
using System.Text.Json.Serialization;
using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Tracking;
using Symphony.Service.Observability;
using Symphony.Workspaces;

namespace Symphony.Service.Hosting;

public sealed class WorkspaceInventoryService(
    RuntimeStateStore state,
    ConfigBackedOptions configOptions,
    ITrackerClient tracker)
{
    private static readonly string[] InventoryStates =
    [
        SymphonyHostedService.TodoState,
        SymphonyHostedService.InProgressState,
        SymphonyHostedService.HumanReviewState,
        SymphonyHostedService.MergingState,
        SymphonyHostedService.ReworkState,
        SymphonyHostedService.DoneState,
        "Duplicate",
        "Canceled",
        "Cancelled"
    ];

    public async Task<WorkspaceInventoryPayload> ListAsync(CancellationToken cancellationToken)
    {
        var snapshot = state.Snapshot();
        var issues = await tracker.FetchIssuesByStatesAsync(InventoryStates, cancellationToken).ConfigureAwait(false);
        var issuesById = issues.ToDictionary(issue => issue.Id, StringComparer.OrdinalIgnoreCase);
        var issuesByIdentifier = issues.ToDictionary(issue => issue.Identifier, StringComparer.OrdinalIgnoreCase);
        var items = new Dictionary<string, WorkspaceInventoryDraft>(StringComparer.OrdinalIgnoreCase);

        foreach (var running in snapshot.Running)
        {
            Upsert(items, running.IssueId, running.IssueIdentifier, running.State, running.WorkspacePath, running.WorkerHost, "running", running.LastEventAt ?? running.StartedAt, running.WorkspaceBaseBranch, running.WorkspaceClean, running.WorkspaceStatus);
        }

        foreach (var retry in snapshot.Retrying)
        {
            Upsert(items, retry.IssueId, retry.IssueIdentifier, "Retrying", retry.WorkspacePath, retry.WorkerHost, "retrying", retry.DueAt, retry.WorkspaceBaseBranch, retry.WorkspaceClean, retry.WorkspaceStatus);
        }

        foreach (var completed in snapshot.Completed)
        {
            Upsert(items, completed.IssueId, completed.IssueIdentifier, completed.State, completed.WorkspacePath, completed.WorkerHost, "completed", completed.CompletedAt, completed.WorkspaceBaseBranch, completed.WorkspaceClean, completed.WorkspaceStatus, completed);
        }

        foreach (var issue in issues)
        {
            var path = ExistingLocalWorkspacePath(issue.Identifier);
            if (path is not null)
            {
                Upsert(items, issue.Id, issue.Identifier, issue.State, path, null, "linear", issue.UpdatedAt ?? issue.CreatedAt, issue.BranchName, null, null);
            }
        }

        foreach (var path in EnumerateLocalWorkspacePaths())
        {
            var identifier = Path.GetFileName(path);
            if (!issuesByIdentifier.TryGetValue(identifier, out var issue))
            {
                Upsert(items, null, identifier, "Unknown", path, null, "local", Directory.GetLastWriteTimeUtc(path), null, null, null);
            }
        }

        var payloadItems = items.Values
            .Select(draft =>
            {
                var issue = draft.IssueId is not null && issuesById.TryGetValue(draft.IssueId, out var byId)
                    ? byId
                    : issuesByIdentifier.GetValueOrDefault(draft.IssueIdentifier ?? "");
                return BuildItem(draft, issue);
            })
            .OrderBy(item => item.CanCleanup ? 0 : 1)
            .ThenByDescending(item => item.LastActivity)
            .ToArray();

        return new WorkspaceInventoryPayload(
            DateTimeOffset.UtcNow,
            configOptions.CurrentConfig().Workspace.Root,
            payloadItems);
    }

    public async Task<WorkspaceInventoryItem> MarkRetainedAsync(string? issueId, string? workspacePath, CancellationToken cancellationToken)
    {
        var item = await FindItemAsync(issueId, workspacePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(item.WorkspacePath))
        {
            throw new InvalidOperationException("Workspace path is required.");
        }

        state.MarkWorkspaceRetained(item.IssueId, item.IssueIdentifier, item.WorkspacePath, "Marked retained by Operator.");
        return (await FindItemAsync(item.IssueId, item.WorkspacePath, cancellationToken).ConfigureAwait(false));
    }

    public async Task<WorkspaceInventoryItem> RequireCleanupAllowedAsync(string? issueId, string? workspacePath, CancellationToken cancellationToken)
    {
        var item = await FindItemAsync(issueId, workspacePath, cancellationToken).ConfigureAwait(false);
        if (!item.CanCleanup)
        {
            throw new InvalidOperationException(item.CleanupBlockedReason);
        }

        return item;
    }

    private async Task<WorkspaceInventoryItem> FindItemAsync(string? issueId, string? workspacePath, CancellationToken cancellationToken)
    {
        var inventory = await ListAsync(cancellationToken).ConfigureAwait(false);
        var item = inventory.Items.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(issueId)
                && (string.Equals(candidate.IssueId, issueId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.IssueIdentifier, issueId, StringComparison.OrdinalIgnoreCase)))
            || (!string.IsNullOrWhiteSpace(workspacePath)
                && !string.IsNullOrWhiteSpace(candidate.WorkspacePath)
                && string.Equals(Path.GetFullPath(candidate.WorkspacePath), Path.GetFullPath(workspacePath), StringComparison.OrdinalIgnoreCase)));

        return item ?? throw new InvalidOperationException("Workspace was not found in the Operator inventory.");
    }

    private WorkspaceInventoryItem BuildItem(WorkspaceInventoryDraft draft, Issue? issue)
    {
        var path = draft.WorkspacePath;
        var local = string.IsNullOrWhiteSpace(draft.WorkerHost);
        var pathExists = local && !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        var git = local && pathExists ? InspectGit(path!) : null;
        var packet = issue is null ? null : ReviewPacketBuilder.Build(issue, running: null, draft.Completed);
        var retained = state.RetainedWorkspace(path);
        var branch = FirstNonEmpty(draft.Branch, issue?.BranchName, git?.Branch);
        var gitStatus = FirstNonEmpty(draft.WorkspaceStatus, git?.Status, pathExists ? "not a git workspace" : null);
        var clean = draft.WorkspaceClean ?? git?.Clean;
        long? diskBytes = pathExists ? DirectorySize(path!) : null;
        var hasRunArtifact = draft.Completed is not null;
        var hasWorkpadArtifact = packet?.WorkpadStatus.StartsWith("complete", StringComparison.OrdinalIgnoreCase) == true;
        var hasPrArtifact = !string.IsNullOrWhiteSpace(packet?.PrUrl);
        var hasDurableArtifacts = hasRunArtifact && (hasPrArtifact || hasWorkpadArtifact);
        var decision = WorkspaceCleanupPolicy.Evaluate(
            issue?.State ?? draft.State,
            pathExists,
            retained is not null,
            hasDurableArtifacts,
            retained?.Reason);

        return new WorkspaceInventoryItem(
            issue?.Id ?? draft.IssueId,
            issue?.Identifier ?? draft.IssueIdentifier ?? Path.GetFileName(path ?? ""),
            issue?.Title ?? "Untracked workspace",
            issue?.State ?? draft.State,
            path,
            draft.WorkerHost,
            branch,
            packet?.PrUrl,
            packet?.WorkpadStatus ?? "not found",
            clean,
            gitStatus,
            diskBytes,
            draft.LastActivity,
            draft.Source,
            pathExists,
            retained is not null,
            retained?.Reason,
            retained?.RetainedAt,
            hasRunArtifact,
            hasPrArtifact,
            hasWorkpadArtifact,
            hasDurableArtifacts,
            decision.CanCleanup,
            decision.Outcome,
            decision.BlockedReason,
            issue?.Url);
    }

    private string? ExistingLocalWorkspacePath(string issueIdentifier)
    {
        var root = configOptions.CurrentConfig().Workspace.Root;
        var path = PathSafety.WorkspacePath(root, PathSafety.SafeIdentifier(issueIdentifier));
        return Directory.Exists(path) ? path : null;
    }

    private IEnumerable<string> EnumerateLocalWorkspacePaths()
    {
        var root = Path.GetFullPath(PathSafety.ExpandHome(configOptions.CurrentConfig().Workspace.Root));
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root).Take(250).ToArray();
    }

    private static void Upsert(
        Dictionary<string, WorkspaceInventoryDraft> items,
        string? issueId,
        string? issueIdentifier,
        string state,
        string? workspacePath,
        string? workerHost,
        string source,
        DateTimeOffset? lastActivity,
        string? branch,
        bool? clean,
        string? status,
        CompletedRunEntry? completed = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return;
        }

        var key = $"{workerHost ?? "local"}:{Path.GetFullPath(workspacePath)}";
        if (!items.TryGetValue(key, out var current))
        {
            items[key] = new WorkspaceInventoryDraft(issueId, issueIdentifier, state, workspacePath, workerHost, source, lastActivity, branch, clean, status, completed);
            return;
        }

        items[key] = current with
        {
            IssueId = current.IssueId ?? issueId,
            IssueIdentifier = current.IssueIdentifier ?? issueIdentifier,
            State = PreferState(current.State, state),
            Source = PreferSource(current.Source, source),
            LastActivity = Latest(current.LastActivity, lastActivity),
            Branch = current.Branch ?? branch,
            WorkspaceClean = current.WorkspaceClean ?? clean,
            WorkspaceStatus = current.WorkspaceStatus ?? status,
            Completed = current.Completed ?? completed
        };
    }

    private static GitInspection InspectGit(string path)
    {
        if (!Directory.Exists(Path.Combine(path, ".git")))
        {
            return new GitInspection(null, false, "not a git workspace");
        }

        try
        {
            var branch = RunGit(path, "rev-parse --abbrev-ref HEAD").Trim();
            var status = RunGit(path, "status --porcelain=v1").Trim();
            return new GitInspection(
                string.IsNullOrWhiteSpace(branch) ? null : branch,
                string.IsNullOrWhiteSpace(status),
                string.IsNullOrWhiteSpace(status) ? "clean" : status);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return new GitInspection(null, false, ex.Message);
        }
    }

    private static string RunGit(string path, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start git.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(5_000);
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("git inspection timed out.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(error.Trim());
        }

        return output;
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });
        }
        catch
        {
            return 0L;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static bool StateEquals(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static DateTimeOffset? Latest(DateTimeOffset? left, DateTimeOffset? right) => left > right ? left : right;

    private static string PreferState(string current, string next)
    {
        if (StateEquals(current, "Unknown") || StateEquals(current, "Retrying"))
        {
            return next;
        }

        return current;
    }

    private static string PreferSource(string current, string next)
    {
        var currentRank = SourceRank(current);
        var nextRank = SourceRank(next);
        return nextRank > currentRank ? next : current;
    }

    private static int SourceRank(string source) => source switch
    {
        "running" => 4,
        "retrying" => 3,
        "completed" => 2,
        "linear" => 1,
        _ => 0
    };

    private sealed record WorkspaceInventoryDraft(
        string? IssueId,
        string? IssueIdentifier,
        string State,
        string? WorkspacePath,
        string? WorkerHost,
        string Source,
        DateTimeOffset? LastActivity,
        string? Branch,
        bool? WorkspaceClean,
        string? WorkspaceStatus,
        CompletedRunEntry? Completed);

    private sealed record GitInspection(string? Branch, bool Clean, string Status);
}

public static class WorkspaceCleanupPolicy
{
    private static readonly string[] NeverCleanStates =
    [
        SymphonyHostedService.TodoState,
        SymphonyHostedService.InProgressState,
        SymphonyHostedService.MergingState,
        SymphonyHostedService.ReworkState
    ];

    private static readonly string[] CleanupEligibleStates =
    [
        SymphonyHostedService.DoneState,
        "Duplicate",
        "Canceled",
        "Cancelled"
    ];

    public static WorkspaceCleanupDecision Evaluate(
        string? state,
        bool pathExists,
        bool retained,
        bool hasDurableArtifacts,
        string? retainedReason = null)
    {
        if (!pathExists)
        {
            return new WorkspaceCleanupDecision(false, "missing", "Workspace path does not exist on this host.");
        }

        if (retained)
        {
            return new WorkspaceCleanupDecision(false, "retained", retainedReason ?? "Marked retained by Operator.");
        }

        if (NeverCleanStates.Any(candidate => StateEquals(state, candidate)))
        {
            return new WorkspaceCleanupDecision(false, "blocked", $"State '{state}' is active and must never be cleaned by Operator.");
        }

        if (StateEquals(state, SymphonyHostedService.HumanReviewState))
        {
            return new WorkspaceCleanupDecision(false, "retained", "Human Review workspaces are retained until approved, reworked, canceled, or explicitly marked safe.");
        }

        if (!CleanupEligibleStates.Any(candidate => StateEquals(state, candidate)))
        {
            return new WorkspaceCleanupDecision(false, "blocked", $"State '{state}' is not cleanup-eligible.");
        }

        if (!hasDurableArtifacts)
        {
            return new WorkspaceCleanupDecision(false, "blocked", "Cleanup requires recorded run artifacts plus a PR URL or complete Codex workpad.");
        }

        return new WorkspaceCleanupDecision(true, "eligible", "Cleanup eligible; durable PR/workpad/run artifacts are recorded.");
    }

    private static bool StateEquals(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

public sealed record WorkspaceCleanupDecision(bool CanCleanup, string Outcome, string BlockedReason);

public sealed record WorkspaceInventoryPayload(
    [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("workspace_root")] string WorkspaceRoot,
    [property: JsonPropertyName("items")] IReadOnlyList<WorkspaceInventoryItem> Items);

public sealed record WorkspaceInventoryItem(
    [property: JsonPropertyName("issue_id")] string? IssueId,
    [property: JsonPropertyName("issue_identifier")] string IssueIdentifier,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("workspace_path")] string? WorkspacePath,
    [property: JsonPropertyName("worker_host")] string? WorkerHost,
    [property: JsonPropertyName("branch")] string? Branch,
    [property: JsonPropertyName("pr_url")] string? PrUrl,
    [property: JsonPropertyName("workpad_status")] string WorkpadStatus,
    [property: JsonPropertyName("git_clean")] bool? GitClean,
    [property: JsonPropertyName("git_status")] string? GitStatus,
    [property: JsonPropertyName("disk_bytes")] long? DiskBytes,
    [property: JsonPropertyName("last_activity")] DateTimeOffset? LastActivity,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("path_exists")] bool PathExists,
    [property: JsonPropertyName("retained")] bool Retained,
    [property: JsonPropertyName("retained_reason")] string? RetainedReason,
    [property: JsonPropertyName("retained_at")] DateTimeOffset? RetainedAt,
    [property: JsonPropertyName("has_run_artifact")] bool HasRunArtifact,
    [property: JsonPropertyName("has_pr_artifact")] bool HasPrArtifact,
    [property: JsonPropertyName("has_workpad_artifact")] bool HasWorkpadArtifact,
    [property: JsonPropertyName("has_durable_artifacts")] bool HasDurableArtifacts,
    [property: JsonPropertyName("can_cleanup")] bool CanCleanup,
    [property: JsonPropertyName("cleanup_outcome")] string CleanupOutcome,
    [property: JsonPropertyName("cleanup_blocked_reason")] string CleanupBlockedReason,
    [property: JsonPropertyName("issue_url")] string? IssueUrl);

public sealed record RetainWorkspaceRequest(
    [property: JsonPropertyName("issue_id")] string? IssueId,
    [property: JsonPropertyName("workspace_path")] string? WorkspacePath);
