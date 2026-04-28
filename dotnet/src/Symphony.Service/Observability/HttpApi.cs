using Symphony.Service.Hosting;

namespace Symphony.Service.Observability;

public static class HttpApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/", (RuntimeStateStore state) =>
        {
            var snapshot = state.Snapshot();
            return Results.Text($"""
                <!doctype html>
                <html>
                <head><title>Symphony Status</title></head>
                <body>
                <h1>Symphony Status</h1>
                <p>Generated at: {snapshot.GeneratedAt:O}</p>
                <p>Running: {snapshot.Running.Count} Retrying: {snapshot.Retrying.Count}</p>
                <p>Total tokens: {snapshot.CodexTotals.TotalTokens}</p>
                <p><a href="/api/v1/state">JSON state</a></p>
                </body>
                </html>
                """, "text/html");
        });

        app.MapGet("/api/v1/state", (RuntimeStateStore state) => Results.Json(ToStatePayload(state.Snapshot())));

        app.MapGet("/api/v1/health", (RuntimeStateStore state, DaemonControlService control) =>
        {
            var snapshot = state.Snapshot();
            return Results.Json(new
            {
                status = "ok",
                generated_at = snapshot.GeneratedAt,
                running = snapshot.Running.Count,
                retrying = snapshot.Retrying.Count,
                operator_actions_available = control.IsBound
            });
        });

        app.MapGet("/api/v1/{issue_identifier}", (string issue_identifier, RuntimeStateStore state) =>
        {
            var snapshot = state.Snapshot();
            var running = snapshot.Running.FirstOrDefault(entry => entry.IssueIdentifier.Equals(issue_identifier, StringComparison.OrdinalIgnoreCase));
            var retry = snapshot.Retrying.FirstOrDefault(entry => entry.IssueIdentifier.Equals(issue_identifier, StringComparison.OrdinalIgnoreCase));

            if (running is null && retry is null)
            {
                return Results.NotFound(new { error = new { code = "issue_not_found", message = "Issue not found" } });
            }

            return Results.Json(new
            {
                issue_identifier,
                issue_id = running?.IssueId ?? retry?.IssueId,
                status = running is not null ? "running" : "retrying",
                workspace = new { path = running?.WorkspacePath ?? retry?.WorkspacePath, host = running?.WorkerHost ?? retry?.WorkerHost },
                attempts = new { restart_count = Math.Max((retry?.Attempt ?? 0) - 1, 0), current_retry_attempt = retry?.Attempt ?? 0 },
                running = running is null ? null : RunningPayload(running),
                retry = retry is null ? null : RetryPayload(retry),
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
        counts = new { running = snapshot.Running.Count, retrying = snapshot.Retrying.Count },
        running = snapshot.Running.Select(RunningPayload),
        retrying = snapshot.Retrying.Select(RetryPayload),
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
        workspace_path = entry.WorkspacePath
    };
}

public sealed record StopRunRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("cleanup_workspace")]
    bool CleanupWorkspace);
