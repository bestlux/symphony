using System.Collections.Concurrent;
using CoreSnapshot = Symphony.Abstractions.Runtime.RuntimeSnapshot;

namespace Symphony.Service.Hosting;

public sealed class RuntimeStateStore
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly ConcurrentDictionary<string, RunningSession> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RetryEntry> _retrying = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _recentEvents = new();
    private PollingStatus _polling = new(false, null, null);
    private CodexTotals _totals = new(0, 0, 0, 0);
    private object? _rateLimits;

    public void SetPolling(PollingStatus polling) => _polling = polling;
    public void UpsertRunning(RunningSession running)
    {
        _running[running.IssueIdentifier] = running;
        if (!string.IsNullOrWhiteSpace(running.LastEvent))
        {
            AddRecentEvent($"{DateTimeOffset.UtcNow:O} {running.IssueIdentifier} {running.LastEvent} {running.LastMessage}");
        }
    }
    public void RemoveRunning(string issueIdentifier) => _running.TryRemove(issueIdentifier, out _);
    public void UpsertRetry(RetryEntry retry) => _retrying[retry.IssueIdentifier] = retry;
    public void RemoveRetry(string issueIdentifier) => _retrying.TryRemove(issueIdentifier, out _);

    public void AddTokens(long inputTokens, long outputTokens, long totalTokens)
    {
        _totals = _totals with
        {
            InputTokens = _totals.InputTokens + inputTokens,
            OutputTokens = _totals.OutputTokens + outputTokens,
            TotalTokens = _totals.TotalTokens + totalTokens,
            SecondsRunning = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds
        };
    }

    public void SetRateLimits(object? rateLimits) => _rateLimits = rateLimits;
    public IReadOnlyList<string> RecentEvents(int count)
    {
        return _recentEvents.Reverse().Take(Math.Clamp(count, 1, 1000)).Reverse().ToArray();
    }

    public RuntimeSnapshot Snapshot() => new(
        DateTimeOffset.UtcNow,
        _polling,
        [.. _running.Values.OrderBy(r => r.IssueIdentifier)],
        [.. _retrying.Values.OrderBy(r => r.DueAt)],
        _totals with { SecondsRunning = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds },
        _rateLimits);

    public void SetFromCore(CoreSnapshot snapshot)
    {
        SetPolling(new PollingStatus(
            snapshot.PollCheckInProgress,
            snapshot.PollingStatus.LastPollCompletedAt,
            snapshot.NextPollDueAt));

        _running.Clear();
        foreach (var running in snapshot.Running)
        {
            UpsertRunning(new RunningSession(
                running.IssueId,
                running.Identifier,
                running.Issue?.State ?? "",
                running.SessionId,
                running.TurnCount,
                running.LastCodexEvent,
                running.LastCodexMessage,
                running.StartedAt,
                running.LastCodexTimestamp,
                running.CodexInputTokens,
                running.CodexOutputTokens,
                running.CodexTotalTokens,
                running.WorkerHost,
                running.WorkspacePath));
        }

        _retrying.Clear();
        foreach (var retry in snapshot.RetryAttempts)
        {
            UpsertRetry(new RetryEntry(
                retry.IssueId,
                retry.Identifier,
                retry.Attempt,
                retry.DueAt,
                retry.Error,
                retry.WorkerHost,
                retry.WorkspacePath));
        }

        _totals = new CodexTotals(
            snapshot.CodexTotals.InputTokens,
            snapshot.CodexTotals.OutputTokens,
            snapshot.CodexTotals.TotalTokens,
            snapshot.CodexTotals.SecondsRunning);
        _rateLimits = snapshot.CodexRateLimits;
    }

    private void AddRecentEvent(string line)
    {
        _recentEvents.Enqueue(line);
        while (_recentEvents.Count > 500 && _recentEvents.TryDequeue(out _))
        {
        }
    }
}

public sealed record RuntimeSnapshot(
    DateTimeOffset GeneratedAt,
    PollingStatus Polling,
    IReadOnlyList<RunningSession> Running,
    IReadOnlyList<RetryEntry> Retrying,
    CodexTotals CodexTotals,
    object? RateLimits);

public sealed record PollingStatus(bool InProgress, DateTimeOffset? LastPollAt, DateTimeOffset? NextPollAt);

public sealed record RunningSession(
    string IssueId,
    string IssueIdentifier,
    string State,
    string? SessionId,
    int TurnCount,
    string? LastEvent,
    string? LastMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastEventAt,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    string? WorkerHost,
    string? WorkspacePath);

public sealed record RetryEntry(
    string IssueId,
    string IssueIdentifier,
    int Attempt,
    DateTimeOffset DueAt,
    string? Error,
    string? WorkerHost,
    string? WorkspacePath);

public sealed record CodexTotals(long InputTokens, long OutputTokens, long TotalTokens, double SecondsRunning);

public sealed class ManualRefreshSignal
{
    private int _pending;

    public bool RequestRefresh() => Interlocked.Exchange(ref _pending, 1) == 0;
    public bool ConsumeRefresh() => Interlocked.Exchange(ref _pending, 0) == 1;
}
