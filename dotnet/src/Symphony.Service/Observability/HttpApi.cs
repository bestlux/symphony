using Symphony.Service.Hosting;
using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Tracking;

namespace Symphony.Service.Observability;

public static class HttpApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/", () => Results.Redirect("/operator"));

        app.MapGet("/operator", () => OperatorIndex());

        app.MapGet("/status", (RuntimeStateStore state) =>
        {
            var snapshot = state.Snapshot();
            return Results.Text($"""
                <!doctype html>
                <html>
                <head><title>Symphony Status</title></head>
                <body>
                <h1>Symphony Status</h1>
                <p>Generated at: {snapshot.GeneratedAt:O}</p>
                <p>Running: {snapshot.Running.Count} Retrying: {snapshot.Retrying.Count} Completed: {snapshot.Completed.Count}</p>
                <p>Total tokens: {snapshot.CodexTotals.TotalTokens}</p>
                <p><a href="/operator">Operator cockpit</a></p>
                <p><a href="/api/v1/state">JSON state</a></p>
                </body>
                </html>
                """, "text/html");
        });

        app.MapGet("/api/v1/state", (RuntimeStateStore state) => Results.Json(ToStatePayload(state.Snapshot())));

        app.MapGet("/api/v1/board", async (
            RuntimeStateStore state,
            ITrackerClient tracker,
            ConfigBackedOptions options,
            CancellationToken cancellationToken) =>
        {
            var config = options.CurrentConfig();
            var workflowStates = new[]
            {
                SymphonyHostedService.TodoState,
                SymphonyHostedService.RunningState,
                SymphonyHostedService.ReadyForReviewState,
                SymphonyHostedService.ReviewingState,
                SymphonyHostedService.BlockedState,
                SymphonyHostedService.DoneState,
                "Merged"
            };
            var issues = await tracker.FetchIssuesByStatesAsync(workflowStates, cancellationToken).ConfigureAwait(false);
            return Results.Json(new
            {
                generated_at = DateTimeOffset.UtcNow,
                lanes = workflowStates.Select(workflowState => new
                {
                    state = workflowState,
                    issues = issues
                        .Where(issue => string.Equals(issue.State, workflowState, StringComparison.OrdinalIgnoreCase))
                        .Select(issue => BoardIssuePayload(issue))
                }),
                runtime = ToStatePayload(state.Snapshot()),
                dispatch_states = config.Tracker.DispatchStates,
                active_states = config.Tracker.ActiveStates
            });
        });

        app.MapGet("/api/v1/health", (RuntimeStateStore state, DaemonControlService control) =>
        {
            var snapshot = state.Snapshot();
            return Results.Json(new
            {
                status = "ok",
                generated_at = snapshot.GeneratedAt,
                running = snapshot.Running.Count,
                retrying = snapshot.Retrying.Count,
                completed = snapshot.Completed.Count,
                operator_actions_available = control.IsBound
            });
        });

        app.MapGet("/api/v1/{issue_identifier}", (string issue_identifier, RuntimeStateStore state) =>
        {
            var snapshot = state.Snapshot();
            var running = snapshot.Running.FirstOrDefault(entry => entry.IssueIdentifier.Equals(issue_identifier, StringComparison.OrdinalIgnoreCase));
            var retry = snapshot.Retrying.FirstOrDefault(entry => entry.IssueIdentifier.Equals(issue_identifier, StringComparison.OrdinalIgnoreCase));
            var completed = snapshot.Completed.FirstOrDefault(entry => entry.IssueIdentifier.Equals(issue_identifier, StringComparison.OrdinalIgnoreCase));

            if (running is null && retry is null && completed is null)
            {
                return Results.NotFound(new { error = new { code = "issue_not_found", message = "Issue not found" } });
            }

            return Results.Json(new
            {
                issue_identifier,
                issue_id = running?.IssueId ?? retry?.IssueId ?? completed?.IssueId,
                status = running is not null ? "running" : retry is not null ? "retrying" : "completed",
                workspace = new
                {
                    path = running?.WorkspacePath ?? retry?.WorkspacePath ?? completed?.WorkspacePath,
                    host = running?.WorkerHost ?? retry?.WorkerHost ?? completed?.WorkerHost,
                    base_commit = running?.WorkspaceBaseCommit ?? retry?.WorkspaceBaseCommit ?? completed?.WorkspaceBaseCommit,
                    base_branch = running?.WorkspaceBaseBranch ?? retry?.WorkspaceBaseBranch ?? completed?.WorkspaceBaseBranch,
                    clean = running?.WorkspaceClean ?? retry?.WorkspaceClean ?? completed?.WorkspaceClean ?? false,
                    status = running?.WorkspaceStatus ?? retry?.WorkspaceStatus ?? completed?.WorkspaceStatus
                },
                attempts = new { restart_count = Math.Max((retry?.Attempt ?? 0) - 1, 0), current_retry_attempt = retry?.Attempt ?? 0 },
                running = running is null ? null : RunningPayload(running),
                retry = retry is null ? null : RetryPayload(retry),
                completed = completed is null ? null : CompletedPayload(completed),
                logs = new { codex_session_logs = Array.Empty<object>() },
                recent_events = running?.LastEventAt is null
                    ? []
                    : new[] { new { at = running.LastEventAt, @event = running.LastEvent, message = running.LastMessage } },
                last_error = retry?.Error,
                tracked = new { }
            });
        });

        app.MapPost("/api/v1/refresh", (ManualRefreshSignal signal) =>
        {
            var queued = signal.RequestRefresh();
            return Results.Accepted(value: new
            {
                queued = true,
                coalesced = !queued,
                requested_at = DateTimeOffset.UtcNow,
                operations = new[] { "poll", "reconcile" }
            });
        });

        app.MapPost("/api/v1/runs/{issue_id}/stop", async (
            string issue_id,
            StopRunRequest? request,
            DaemonControlService control,
            CancellationToken cancellationToken) =>
        {
            await control.StopRunAsync(issue_id, request?.CleanupWorkspace ?? false, cancellationToken).ConfigureAwait(false);
            return Results.Accepted(value: new
            {
                accepted = true,
                issue_id,
                cleanup_workspace = request?.CleanupWorkspace ?? false
            });
        });

        app.MapPost("/api/v1/runs/{issue_id}/retry", async (
            string issue_id,
            DaemonControlService control,
            CancellationToken cancellationToken) =>
        {
            await control.RetryRunAsync(issue_id, cancellationToken).ConfigureAwait(false);
            return Results.Accepted(value: new { accepted = true, issue_id });
        });

        app.MapPost("/api/v1/issues/{issue_id}/state", async (
            string issue_id,
            UpdateIssueStateRequest request,
            ITrackerClient tracker,
            RuntimeStateStore state,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.State))
            {
                return Results.BadRequest(new { error = new { code = "missing_state", message = "State is required." } });
            }

            await tracker.UpdateIssueStateAsync(issue_id, request.State, cancellationToken).ConfigureAwait(false);
            state.RecordIssueStateTransition(issue_id, $"Operator moved issue to {request.State}");
            return Results.Accepted(value: new { accepted = true, issue_id, state = request.State });
        });

        app.MapPost("/api/v1/issues/{issue_id}/merge", async (
            string issue_id,
            MergeWorkspaceRequest? request,
            MergeWorkflowService merge,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await merge.MergeAsync(
                    issue_id,
                    request?.CleanupWorkspace ?? false,
                    request?.WorkspacePath,
                    cancellationToken).ConfigureAwait(false);
                return Results.Accepted(value: new
                {
                    accepted = true,
                    issue_id = result.IssueId,
                    issue_identifier = result.IssueIdentifier,
                    workspace_path = result.WorkspacePath,
                    commit_sha = result.CommitSha,
                    cleanup_outcome = result.CleanupOutcome,
                    changed_files = result.ChangedFiles,
                    validation_output = result.ValidationOutput
                });
            }
            catch (Exception ex)
                when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new { error = new { code = "merge_failed", message = ex.Message } });
            }
        });

        app.MapPost("/api/v1/workspaces/cleanup", async (
            CleanupWorkspaceRequest request,
            MergeWorkflowService merge,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await merge.CleanupAsync(
                    request.IssueId,
                    request.WorkspacePath,
                    request.Force,
                    cancellationToken).ConfigureAwait(false);
                return Results.Accepted(value: new
                {
                    accepted = true,
                    issue_id = result.IssueId,
                    workspace_path = result.WorkspacePath,
                    cleanup_outcome = result.CleanupOutcome
                });
            }
            catch (Exception ex)
                when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new { error = new { code = "cleanup_failed", message = ex.Message } });
            }
        });

        app.MapPost("/api/v1/workspaces/open", (OpenWorkspaceRequest request, ConfigBackedOptions options) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path))
            {
                return Results.BadRequest(new { error = new { code = "missing_path", message = "Path is required." } });
            }

            var fullPath = Path.GetFullPath(request.Path);
            var workspaceRoot = Path.GetFullPath(options.CurrentConfig().Workspace.Root);
            if (!IsPathWithin(fullPath, workspaceRoot))
            {
                return Results.BadRequest(new { error = new { code = "workspace_outside_root", message = "Workspace path is outside the configured workspace root." } });
            }

            if (!Directory.Exists(fullPath))
            {
                return Results.BadRequest(new { error = new { code = "workspace_not_found", message = "Workspace path does not exist." } });
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(fullPath);
            System.Diagnostics.Process.Start(startInfo);

            return Results.Accepted(value: new { accepted = true, path = fullPath });
        });

        app.MapGet("/api/v1/logs/recent", (int? count, RuntimeStateStore state) =>
        {
            return Results.Json(new
            {
                generated_at = DateTimeOffset.UtcNow,
                lines = state.RecentEvents(count ?? 200)
            });
        });
    }

    private static object ToStatePayload(RuntimeSnapshot snapshot) => new
    {
        generated_at = snapshot.GeneratedAt,
        counts = new { running = snapshot.Running.Count, retrying = snapshot.Retrying.Count, completed = snapshot.Completed.Count },
        running = snapshot.Running.Select(RunningPayload),
        retrying = snapshot.Retrying.Select(RetryPayload),
        completed = snapshot.Completed.Select(CompletedPayload),
        codex_totals = new
        {
            input_tokens = snapshot.CodexTotals.InputTokens,
            output_tokens = snapshot.CodexTotals.OutputTokens,
            total_tokens = snapshot.CodexTotals.TotalTokens,
            seconds_running = snapshot.CodexTotals.SecondsRunning
        },
        rate_limits = snapshot.RateLimits
    };

    private static object RunningPayload(RunningSession entry) => new
    {
        issue_id = entry.IssueId,
        issue_identifier = entry.IssueIdentifier,
        state = entry.State,
        worker_host = entry.WorkerHost,
        workspace_path = entry.WorkspacePath,
        workspace_base_commit = entry.WorkspaceBaseCommit,
        workspace_base_branch = entry.WorkspaceBaseBranch,
        workspace_clean = entry.WorkspaceClean,
        workspace_status = entry.WorkspaceStatus,
        session_id = entry.SessionId,
        turn_count = entry.TurnCount,
        last_event = entry.LastEvent,
        last_message = entry.LastMessage,
        started_at = entry.StartedAt,
        last_event_at = entry.LastEventAt,
        tokens = new
        {
            input_tokens = entry.InputTokens,
            output_tokens = entry.OutputTokens,
            total_tokens = entry.TotalTokens
        }
    };

    private static object RetryPayload(RetryEntry entry) => new
    {
        issue_id = entry.IssueId,
        issue_identifier = entry.IssueIdentifier,
        attempt = entry.Attempt,
        due_at = entry.DueAt,
        error = entry.Error,
        worker_host = entry.WorkerHost,
        workspace_path = entry.WorkspacePath,
        workspace_base_commit = entry.WorkspaceBaseCommit,
        workspace_base_branch = entry.WorkspaceBaseBranch,
        workspace_clean = entry.WorkspaceClean,
        workspace_status = entry.WorkspaceStatus
    };

    private static object CompletedPayload(CompletedRunEntry entry) => new
    {
        issue_id = entry.IssueId,
        issue_identifier = entry.IssueIdentifier,
        state = entry.State,
        status = entry.Status,
        worker_host = entry.WorkerHost,
        workspace_path = entry.WorkspacePath,
        workspace_base_commit = entry.WorkspaceBaseCommit,
        workspace_base_branch = entry.WorkspaceBaseBranch,
        workspace_clean = entry.WorkspaceClean,
        workspace_status = entry.WorkspaceStatus,
        session_id = entry.SessionId,
        thread_id = entry.ThreadId,
        turn_id = entry.TurnId,
        turn_count = entry.TurnCount,
        last_event = entry.LastEvent,
        last_message = entry.LastMessage,
        error = entry.Error,
        started_at = entry.StartedAt,
        completed_at = entry.CompletedAt,
        cleanup_outcome = entry.CleanupOutcome,
        tokens = new
        {
            input_tokens = entry.InputTokens,
            output_tokens = entry.OutputTokens,
            total_tokens = entry.TotalTokens
        }
    };

    private static object BoardIssuePayload(Issue issue) => new
    {
        issue_id = issue.Id,
        issue_identifier = issue.Identifier,
        title = issue.Title,
        description = issue.Description,
        state = issue.State,
        priority = issue.Priority,
        branch_name = issue.BranchName,
        url = issue.Url,
        labels = issue.Labels,
        updated_at = issue.UpdatedAt,
        created_at = issue.CreatedAt,
        blocked_by = issue.BlockedBy.Select(blocker => new
        {
            id = blocker.Id,
            identifier = blocker.Identifier,
            state = blocker.State
        })
    };

    private static IResult OperatorIndex()
    {
        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "operator", "index.html");
        if (!File.Exists(indexPath))
        {
            return Results.Text("""
                <!doctype html>
                <html>
                <head><title>Symphony Operator</title></head>
                <body>
                <h1>Symphony Operator web assets have not been built.</h1>
                <p>Run <code>npm install</code> and <code>npm run build</code> in <code>dotnet/src/Symphony.Operator.Web</code>.</p>
                </body>
                </html>
                """, "text/html");
        }

        return Results.File(indexPath, "text/html");
    }

    private static bool IsPathWithin(string path, string root)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return relativePath == "."
            || (!relativePath.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relativePath));
    }
}

public sealed record StopRunRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("cleanup_workspace")]
    bool CleanupWorkspace);

public sealed record UpdateIssueStateRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("state")]
    string State);

public sealed record OpenWorkspaceRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("path")]
    string Path);

public sealed record MergeWorkspaceRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("cleanup_workspace")]
    bool CleanupWorkspace,
    [property: System.Text.Json.Serialization.JsonPropertyName("workspace_path")]
    string? WorkspacePath);

public sealed record CleanupWorkspaceRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("issue_id")]
    string? IssueId,
    [property: System.Text.Json.Serialization.JsonPropertyName("workspace_path")]
    string? WorkspacePath,
    [property: System.Text.Json.Serialization.JsonPropertyName("force")]
    bool Force);
