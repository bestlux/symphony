namespace Symphony.Service.Hosting;

public sealed class DaemonControlService
{
    private Func<string, bool, CancellationToken, Task>? _stopRun;
    private Func<string, CancellationToken, Task>? _retryRun;
    private Func<CancellationToken, Task>? _refresh;

    public void Bind(
        Func<string, bool, CancellationToken, Task> stopRun,
        Func<string, CancellationToken, Task> retryRun,
        Func<CancellationToken, Task> refresh)
    {
        _stopRun = stopRun;
        _retryRun = retryRun;
        _refresh = refresh;
    }

    public bool IsBound => _stopRun is not null && _retryRun is not null && _refresh is not null;

    public Task StopRunAsync(string issueId, bool cleanupWorkspace, CancellationToken cancellationToken)
    {
        return _stopRun?.Invoke(issueId, cleanupWorkspace, cancellationToken) ?? Task.CompletedTask;
    }

    public Task RetryRunAsync(string issueId, CancellationToken cancellationToken)
    {
        return _retryRun?.Invoke(issueId, cancellationToken) ?? Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        return _refresh?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }
}
