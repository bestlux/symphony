using System.Diagnostics;
using System.Text;
using Symphony.Abstractions.Tracking;
using Symphony.Core.Configuration;

namespace Symphony.Service.Hosting;

public sealed class MergeWorkflowService(
    ConfigBackedOptions configOptions,
    RuntimeStateStore state,
    ITrackerClient tracker,
    ILogger<MergeWorkflowService> logger)
{
    private static readonly string[] MergeableStates = [SymphonyHostedService.MergingState];
    private static readonly string[] CleanupStates = ["Done", "Merged", "Canceled", "Cancelled", "Duplicate"];

    public async Task<MergeWorkspaceResult> MergeAsync(string issueId, bool cleanupWorkspace, string? requestedWorkspace, CancellationToken cancellationToken)
    {
        var snapshot = state.Snapshot();
        if (snapshot.Running.Any(run => MatchesIssue(run.IssueId, run.IssueIdentifier, issueId)))
        {
            throw new InvalidOperationException("Cannot merge while the issue has an active run.");
        }

        var issue = await FetchIssueAsync(issueId, cancellationToken).ConfigureAwait(false);
        if (!MergeableStates.Any(allowed => string.Equals(issue.State, allowed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Legacy local merge requires issue state '{SymphonyHostedService.MergingState}'. Current state is '{issue.State}'.");
        }

        var completed = FindLatestCompleted(issueId, requestedWorkspace)
            ?? throw new InvalidOperationException("No completed run with a workspace was found for this issue.");
        var workspace = ValidateWorkspacePath(requestedWorkspace ?? completed.WorkspacePath);
        var repoRoot = Directory.GetCurrentDirectory();
        EnsureCleanRepo(repoRoot);

        var changedFiles = await ApplyWorkspaceChangesAsync(repoRoot, workspace, cancellationToken).ConfigureAwait(false);
        if (changedFiles.Count == 0)
        {
            throw new InvalidOperationException("Workspace has no mergeable changes.");
        }

        var validation = await RunValidationAsync(repoRoot, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repoRoot, "add -A", cancellationToken).ConfigureAwait(false);

        var status = await RunGitAsync(repoRoot, "status --porcelain", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(status.Output))
        {
            throw new InvalidOperationException("Validation completed but no repository changes remained to commit.");
        }

        var subject = $"chore(orchestration): merge {issue.Identifier} workspace";
        await RunGitAsync(repoRoot, $"commit -m {Quote(subject)}", cancellationToken).ConfigureAwait(false);
        var sha = (await RunGitAsync(repoRoot, "rev-parse --short HEAD", cancellationToken).ConfigureAwait(false)).Output.Trim();

        var cleanupOutcome = "merged";
        if (cleanupWorkspace)
        {
            DeleteWorkspace(workspace);
            cleanupOutcome = "merged+cleaned";
        }

        var comment = $"""
            Legacy local merge by Symphony Operator.

            Commit: `{sha}`
            Workspace: `{workspace}`
            Validation: `{validation.Command}` passed
            Cleanup: `{cleanupOutcome}`
            """;

        await tracker.CreateCommentAsync(issue.Id, comment, cancellationToken).ConfigureAwait(false);
        try
        {
            await tracker.UpdateIssueStateAsync(issue.Id, SymphonyHostedService.DoneState, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Could not move {IssueIdentifier} to Done after legacy local merge; leaving Linear state unchanged", issue.Identifier);
            await tracker.CreateCommentAsync(issue.Id, "Symphony could not move this issue to `Done` after legacy local merge; check the Linear workflow state configuration.", cancellationToken).ConfigureAwait(false);
        }

        state.RecordCompletion(completed with
        {
            State = SymphonyHostedService.DoneState,
            Status = "LegacyMerged",
            LastEvent = "workspace/merged",
            LastMessage = comment,
            CompletedAt = DateTimeOffset.UtcNow,
            CleanupOutcome = cleanupOutcome
        });

        return new MergeWorkspaceResult(issue.Id, issue.Identifier, workspace, sha, cleanupOutcome, changedFiles, validation.Output);
    }

    public async Task<CleanupWorkspaceResult> CleanupAsync(string? issueId, string? requestedWorkspace, bool force, CancellationToken cancellationToken)
    {
        var snapshot = state.Snapshot();
        var completed = string.IsNullOrWhiteSpace(issueId) && string.IsNullOrWhiteSpace(requestedWorkspace)
            ? null
            : FindLatestCompleted(issueId ?? "", requestedWorkspace);
        var workspace = ValidateWorkspacePath(requestedWorkspace ?? completed?.WorkspacePath);

        if (snapshot.Running.Any(run => SamePath(run.WorkspacePath, workspace)))
        {
            throw new InvalidOperationException("Cannot clean an active workspace.");
        }

        if (!force)
        {
            var merged = completed is not null
                && (string.Equals(completed.Status, "Merged", StringComparison.OrdinalIgnoreCase)
                    || completed.CleanupOutcome.Contains("merged", StringComparison.OrdinalIgnoreCase));
            if (!merged && !string.IsNullOrWhiteSpace(issueId))
            {
                var issue = await FetchIssueAsync(issueId, cancellationToken).ConfigureAwait(false);
                merged = CleanupStates.Any(allowed => string.Equals(issue.State, allowed, StringComparison.OrdinalIgnoreCase));
            }

            if (!merged)
            {
                throw new InvalidOperationException("Workspace is not marked merged. Pass force only after manually verifying artifacts are no longer needed.");
            }
        }

        DeleteWorkspace(workspace);
        if (completed is not null)
        {
            state.RecordCompletion(completed with
            {
                Status = "Cleaned",
                LastEvent = "workspace/cleaned",
                LastMessage = $"Workspace cleaned: {workspace}",
                CompletedAt = DateTimeOffset.UtcNow,
                CleanupOutcome = "cleaned"
            });
        }

        return new CleanupWorkspaceResult(issueId, workspace, "cleaned");
    }

    private CompletedRunEntry? FindLatestCompleted(string issueId, string? requestedWorkspace)
    {
        return state.Snapshot().Completed
            .Where(entry => !string.IsNullOrWhiteSpace(entry.WorkspacePath))
            .Where(entry => string.IsNullOrWhiteSpace(issueId) || MatchesIssue(entry.IssueId, entry.IssueIdentifier, issueId))
            .Where(entry => string.IsNullOrWhiteSpace(requestedWorkspace) || SamePath(entry.WorkspacePath, requestedWorkspace))
            .OrderByDescending(entry => entry.CompletedAt)
            .FirstOrDefault();
    }

    private async Task<Symphony.Abstractions.Issues.Issue> FetchIssueAsync(string issueId, CancellationToken cancellationToken)
    {
        var issues = await tracker.FetchIssueStatesByIdsAsync([issueId], cancellationToken).ConfigureAwait(false);
        return issues.FirstOrDefault()
            ?? throw new InvalidOperationException($"Issue '{issueId}' was not found in Linear.");
    }

    private string ValidateWorkspacePath(string? workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            throw new InvalidOperationException("Workspace path is required.");
        }

        var fullPath = Path.GetFullPath(workspace);
        var root = Path.GetFullPath(configOptions.CurrentConfig().Workspace.Root);
        if (!IsPathWithin(fullPath, root))
        {
            throw new InvalidOperationException("Workspace path is outside the configured workspace root.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new InvalidOperationException("Workspace path does not exist.");
        }

        return fullPath;
    }

    private static async Task<IReadOnlyList<string>> ApplyWorkspaceChangesAsync(string repoRoot, string workspace, CancellationToken cancellationToken)
    {
        var changedFiles = new List<string>();
        var patch = await RunGitAsync(workspace, "diff --binary HEAD", cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(patch.Output))
        {
            var patchPath = Path.Combine(Path.GetTempPath(), $"symphony-merge-{Guid.NewGuid():N}.patch");
            await File.WriteAllTextAsync(patchPath, patch.Output, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            try
            {
                await RunGitAsync(repoRoot, $"apply --index --3way {Quote(patchPath)}", cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(patchPath);
            }
        }

        var untracked = await RunGitAsync(workspace, "ls-files --others --exclude-standard", cancellationToken).ConfigureAwait(false);
        foreach (var relative in untracked.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var source = Path.GetFullPath(Path.Combine(workspace, relative));
            var destination = Path.GetFullPath(Path.Combine(repoRoot, relative));
            if (!IsPathWithin(source, workspace) || !IsPathWithin(destination, repoRoot) || !File.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            await RunGitAsync(repoRoot, $"add {Quote(relative)}", cancellationToken).ConfigureAwait(false);
        }

        var status = await RunGitAsync(repoRoot, "status --porcelain", cancellationToken).ConfigureAwait(false);
        changedFiles.AddRange(status.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Length > 3 ? line[3..] : line));
        return changedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<CommandResult> RunValidationAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var script = Path.Combine(repoRoot, "scripts", "validate-symphony.ps1");
        var result = await RunProcessAsync(
            repoRoot,
            "powershell",
            $"-NoProfile -ExecutionPolicy Bypass -File {Quote(script)}",
            cancellationToken).ConfigureAwait(false);
        return result with { Command = ".\\scripts\\validate-symphony.ps1" };
    }

    private static void EnsureCleanRepo(string repoRoot)
    {
        var status = RunGitAsync(repoRoot, "status --porcelain", CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Output;
        if (!string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException($"Target checkout is dirty before merge: {status}");
        }
    }

    private void DeleteWorkspace(string workspace)
    {
        var root = Path.GetFullPath(configOptions.CurrentConfig().Workspace.Root);
        if (!IsPathWithin(workspace, root) || string.Equals(Path.GetFullPath(workspace), root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete invalid workspace path.");
        }

        WorkspaceDirectory.Delete(workspace);
    }

    private static Task<CommandResult> RunGitAsync(string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        return RunProcessAsync(workingDirectory, "git", arguments, cancellationToken);
    }

    private static async Task<CommandResult> RunProcessAsync(string workingDirectory, string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        var combined = string.Join(Environment.NewLine, new[] { output, error }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments} failed with exit code {process.ExitCode}: {combined}");
        }

        return new CommandResult(fileName + " " + arguments, combined);
    }

    private static bool MatchesIssue(string issueId, string issueIdentifier, string candidate)
    {
        return string.Equals(issueId, candidate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(issueIdentifier, candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathWithin(string path, string root)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return relativePath == "."
            || (!relativePath.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relativePath));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}

internal static class WorkspaceDirectory
{
    public static void Delete(string workspace)
    {
        NormalizeAttributes(workspace);
        Directory.Delete(workspace, recursive: true);
    }

    private static void NormalizeAttributes(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        File.SetAttributes(path, FileAttributes.Normal);
    }
}

public sealed record MergeWorkspaceResult(
    string IssueId,
    string IssueIdentifier,
    string WorkspacePath,
    string CommitSha,
    string CleanupOutcome,
    IReadOnlyList<string> ChangedFiles,
    string ValidationOutput);

public sealed record CleanupWorkspaceResult(string? IssueId, string WorkspacePath, string CleanupOutcome);

internal sealed record CommandResult(string Command, string Output);
