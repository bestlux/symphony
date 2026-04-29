using Symphony.Service.Hosting;
using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Tracking;
using Symphony.Core.Configuration;

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

        app.MapGet("/api/v1/state", (RuntimeStateStore state, ConfigBackedOptions options) =>
            Results.Json(ToStatePayload(state.Snapshot(), options.CurrentConfig().Observability)));

        app.MapGet("/api/v1/board", async (
            RuntimeStateStore state,
            ITrackerClient tracker,
            ConfigBackedOptions options,
            CancellationToken cancellationToken) =>
        {
            var config = options.CurrentConfig();
            var snapshot = state.Snapshot();
            var runningByIssue = snapshot.Running.ToDictionary(entry => entry.IssueId, StringComparer.Ordinal);
            var completedByIssue = LatestCompletedByIssue(snapshot);
            var workflowStates = new[]
            {
                SymphonyHostedService.TodoState,
                SymphonyHostedService.InProgressState,
                SymphonyHostedService.HumanReviewState,
                SymphonyHostedService.MergingState,
                SymphonyHostedService.ReworkState,
                SymphonyHostedService.DoneState
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
                        .Select(issue => BoardIssuePayload(
                            issue,
                            runningByIssue.GetValueOrDefault(issue.Id),
                            completedByIssue.GetValueOrDefault(issue.Id)))
                }),
                runtime = ToStatePayload(snapshot, config.Observability),
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

        app.MapGet("/api/v1/{issue_identifier}", (string issue_identifier, RuntimeStateStore state, ConfigBackedOptions options) =>
        {
            var config = options.CurrentConfig();
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
                running = running is null ? null : RunningPayload(running, config.Observability),
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

        app.MapPost("/api/v1/service/restart", (ServiceRestartService restart) =>
        {
            var result = restart.RequestRestart();
            return Results.Accepted(value: new
            {
                accepted = true,
                restarting = true,
                current_process_id = result.CurrentProcessId,
                executable_path = result.ExecutablePath,
                working_directory = result.WorkingDirectory,
                requested_at = DateTimeOffset.UtcNow
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

            if (string.Equals(request.State, SymphonyHostedService.HumanReviewState, StringComparison.OrdinalIgnoreCase))
            {
                var issues = await tracker.FetchIssueStatesByIdsAsync([issue_id], cancellationToken).ConfigureAwait(false);
                var issue = issues.FirstOrDefault();
                if (issue is null)
                {
                    return Results.BadRequest(new { error = new { code = "issue_not_found", message = "Issue was not found in the tracker." } });
                }

                var snapshot = state.Snapshot();
                var packet = ReviewPacketBuilder.Build(
                    issue,
                    snapshot.Running.FirstOrDefault(entry => entry.IssueId == issue.Id),
                    snapshot.Completed
                        .Where(entry => entry.IssueId == issue.Id)
                        .OrderByDescending(entry => entry.CompletedAt)
                        .FirstOrDefault());
                if (!packet.ReadyForHumanReview)
                {
                    return Results.BadRequest(new
                    {
                        error = new
                        {
                            code = "review_packet_incomplete",
                            message = "Human Review requires a PR URL, complete Codex workpad, summary, changed files, and validation evidence.",
                            missing = packet.Missing,
                            review_packet = packet
                        }
                    });
                }
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
            WorkspaceInventoryService inventory,
            MergeWorkflowService merge,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await inventory.RequireCleanupAllowedAsync(
                    request.IssueId,
                    request.WorkspacePath,
                    cancellationToken).ConfigureAwait(false);
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

        app.MapGet("/api/v1/workspaces", async (
            WorkspaceInventoryService inventory,
            CancellationToken cancellationToken) =>
        {
            return Results.Json(await inventory.ListAsync(cancellationToken).ConfigureAwait(false));
        });

        app.MapPost("/api/v1/workspaces/retain", async (
            RetainWorkspaceRequest request,
            WorkspaceInventoryService inventory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var item = await inventory.MarkRetainedAsync(
                    request.IssueId,
                    request.WorkspacePath,
                    cancellationToken).ConfigureAwait(false);
                return Results.Accepted(value: new { accepted = true, workspace = item });
            }
            catch (Exception ex)
                when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new { error = new { code = "retain_failed", message = ex.Message } });
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

    private static object ToStatePayload(RuntimeSnapshot snapshot, ObservabilityConfig observability) => new
    {
        generated_at = snapshot.GeneratedAt,
        counts = new { running = snapshot.Running.Count, retrying = snapshot.Retrying.Count, completed = snapshot.Completed.Count },
        running = snapshot.Running.Select(entry => RunningPayload(entry, observability)),
        retrying = snapshot.Retrying.Select(RetryPayload),
        completed = snapshot.Completed.Select(CompletedPayload),
        polling = new
        {
            in_progress = snapshot.Polling.InProgress,
            last_poll_at = snapshot.Polling.LastPollAt,
            next_poll_at = snapshot.Polling.NextPollAt
        },
        heartbeat = new
        {
            quiet_threshold_ms = observability.QuietThresholdMs,
            stale_threshold_ms = observability.StaleThresholdMs
        },
        codex_totals = new
        {
            input_tokens = snapshot.CodexTotals.InputTokens,
            output_tokens = snapshot.CodexTotals.OutputTokens,
            total_tokens = snapshot.CodexTotals.TotalTokens,
            seconds_running = snapshot.CodexTotals.SecondsRunning
        },
        rate_limits = snapshot.RateLimits
    };

    private static object RunningPayload(RunningSession entry, ObservabilityConfig observability)
    {
        var heartbeat = RunHeartbeat(entry, observability);
        return new
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
            thread_id = entry.ThreadId,
            turn_id = entry.TurnId,
            codex_app_server_pid = entry.CodexAppServerPid,
            turn_count = entry.TurnCount,
            retry_attempt = entry.RetryAttempt,
            last_event = entry.LastEvent,
            last_message = entry.LastMessage,
            last_meaningful_event_category = MeaningfulEventCategory(entry.LastEvent),
            started_at = entry.StartedAt,
            last_event_at = entry.LastEventAt,
            heartbeat_at = heartbeat.Timestamp,
            heartbeat_age_ms = heartbeat.AgeMs,
            heartbeat_status = heartbeat.Status,
            quiet_threshold_ms = observability.QuietThresholdMs,
            stale_threshold_ms = observability.StaleThresholdMs,
            stale = heartbeat.Stale,
            tokens = new
            {
                input_tokens = entry.InputTokens,
                output_tokens = entry.OutputTokens,
                total_tokens = entry.TotalTokens
            }
        };
    }

    private static RunHeartbeatPayload RunHeartbeat(RunningSession entry, ObservabilityConfig observability)
    {
        var timestamp = entry.LastEventAt ?? entry.StartedAt;
        var ageMs = Math.Max(0, (long)(DateTimeOffset.UtcNow - timestamp).TotalMilliseconds);
        var status = ageMs <= observability.QuietThresholdMs
            ? "Active"
            : ageMs <= observability.StaleThresholdMs
                ? "Quiet"
                : "Stale";

        return new RunHeartbeatPayload(timestamp, ageMs, status, string.Equals(status, "Stale", StringComparison.Ordinal));
    }

    private static string MeaningfulEventCategory(string? lastEvent)
    {
        if (string.IsNullOrWhiteSpace(lastEvent))
        {
            return "started";
        }

        if (lastEvent.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return "tokens";
        }

        if (lastEvent.Contains("tool", StringComparison.OrdinalIgnoreCase))
        {
            return "tool";
        }

        if (lastEvent.Contains("message", StringComparison.OrdinalIgnoreCase)
            || lastEvent.Contains("response", StringComparison.OrdinalIgnoreCase))
        {
            return "message";
        }

        if (string.Equals(lastEvent, "stdout", StringComparison.OrdinalIgnoreCase)
            || string.Equals(lastEvent, "stderr", StringComparison.OrdinalIgnoreCase))
        {
            return "log";
        }

        return lastEvent;
    }

    private sealed record RunHeartbeatPayload(
        DateTimeOffset Timestamp,
        long AgeMs,
        string Status,
        bool Stale);

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

    private static IReadOnlyDictionary<string, CompletedRunEntry> LatestCompletedByIssue(RuntimeSnapshot snapshot)
    {
        var entries = new Dictionary<string, CompletedRunEntry>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Completed.OrderByDescending(entry => entry.CompletedAt))
        {
            entries.TryAdd(entry.IssueId, entry);
        }

        return entries;
    }

    private static object BoardIssuePayload(
        Issue issue,
        RunningSession? running,
        CompletedRunEntry? completed) => new
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
        review_packet = ReviewPacketBuilder.Build(issue, running, completed),
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
