using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using Symphony.Operator.Commands;
using Symphony.Operator.Models;
using Symphony.Operator.Services;

namespace Symphony.Operator.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SymphonyApiClient _apiClient;
    private readonly CancellationTokenSource _disposeCts = new();
    private RunDto? _selectedRun;
    private RetryDto? _selectedRetry;
    private CompletedRunDto? _selectedCompleted;
    private string _connectionStatus = "Connecting";
    private string _lastError = "";
    private string _pollingStatus = "";
    private string _tokenSummary = "";
    private IReadOnlyList<string> _recentLogs = [];

    public MainWindowViewModel()
        : this(new SymphonyApiClient())
    {
    }

    public MainWindowViewModel(SymphonyApiClient apiClient)
    {
        _apiClient = apiClient;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        StopCommand = new AsyncRelayCommand(StopSelectedAsync, () => SelectedRun is not null);
        RetryCommand = new AsyncRelayCommand(RetrySelectedAsync, () => SelectedRun is not null || SelectedRetry is not null);
        OpenWorkspaceCommand = new AsyncRelayCommand(OpenWorkspaceAsync, HasWorkspace);
        OpenIssueCommand = new AsyncRelayCommand(OpenIssueAsync, () => SelectedIdentifier.Length > 0);
        _ = PollAsync(_disposeCts.Token);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RunDto> Running { get; } = [];
    public ObservableCollection<RetryDto> Retrying { get; } = [];
    public ObservableCollection<CompletedRunDto> Completed { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public AsyncRelayCommand OpenWorkspaceCommand { get; }
    public AsyncRelayCommand OpenIssueCommand { get; }

    public RunDto? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (SetField(ref _selectedRun, value))
            {
                if (value is not null)
                {
                    SelectedRetry = null;
                    SelectedCompleted = null;
                }

                RaiseSelectionDependentCommands();
            }
        }
    }

    public RetryDto? SelectedRetry
    {
        get => _selectedRetry;
        set
        {
            if (SetField(ref _selectedRetry, value))
            {
                if (value is not null)
                {
                    SelectedRun = null;
                    SelectedCompleted = null;
                }

                RaiseSelectionDependentCommands();
            }
        }
    }

    public CompletedRunDto? SelectedCompleted
    {
        get => _selectedCompleted;
        set
        {
            if (SetField(ref _selectedCompleted, value))
            {
                if (value is not null)
                {
                    SelectedRun = null;
                    SelectedRetry = null;
                }

                RaiseSelectionDependentCommands();
            }
        }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetField(ref _connectionStatus, value);
    }

    public string LastError
    {
        get => _lastError;
        private set => SetField(ref _lastError, value);
    }

    public string PollingStatus
    {
        get => _pollingStatus;
        private set => SetField(ref _pollingStatus, value);
    }

    public string TokenSummary
    {
        get => _tokenSummary;
        private set => SetField(ref _tokenSummary, value);
    }

    public IReadOnlyList<string> RecentLogs
    {
        get => _recentLogs;
        private set => SetField(ref _recentLogs, value);
    }

    public string SelectedIdentifier => SelectedRun?.IssueIdentifier ?? SelectedRetry?.IssueIdentifier ?? SelectedCompleted?.IssueIdentifier ?? "";
    public string SelectedIssueId => SelectedRun?.IssueId ?? SelectedRetry?.IssueId ?? SelectedCompleted?.IssueId ?? "";
    public string SelectedWorkspace => SelectedRun?.WorkspacePath ?? SelectedRetry?.WorkspacePath ?? SelectedCompleted?.WorkspacePath ?? "";
    public string SelectedLastMessage => SelectedRun?.LastMessage ?? SelectedRetry?.Error ?? SelectedCompletedSummary();

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await _apiClient.GetStateAsync(cancellationToken).ConfigureAwait(true);
            var health = await _apiClient.GetHealthAsync(cancellationToken).ConfigureAwait(true);
            var logs = await _apiClient.GetRecentLogsAsync(120, cancellationToken).ConfigureAwait(true);
            if (state is null)
            {
                ConnectionStatus = "No state";
                return;
            }

            var selectedRunIssueId = SelectedRun?.IssueId;
            var selectedRetryIssueId = SelectedRetry?.IssueId;
            var selectedCompletedIssueId = SelectedCompleted?.IssueId;
            Replace(Running, state.Running);
            Replace(Retrying, state.Retrying);
            Replace(Completed, state.Completed);
            RestoreSelection(selectedRunIssueId, selectedRetryIssueId, selectedCompletedIssueId);
            ConnectionStatus = health is null ? "Connected" : $"Connected - actions {(health.OperatorActionsAvailable ? "ready" : "not ready")}";
            PollingStatus = $"Running {state.Counts.Running} | Retrying {state.Counts.Retrying} | Completed {state.Counts.Completed} | Generated {state.GeneratedAt:T}";
            TokenSummary = $"Tokens {state.CodexTotals.TotalTokens:N0} in {state.CodexTotals.SecondsRunning:N0}s";
            RecentLogs = logs;
            LastError = "";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ConnectionStatus = "Disconnected";
            LastError = ex.Message;
        }
    }

    private async Task StopSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedRun is null)
        {
            return;
        }

        await _apiClient.StopRunAsync(SelectedRun.IssueId, cleanupWorkspace: false, cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task RetrySelectedAsync(CancellationToken cancellationToken)
    {
        var issueId = SelectedIssueId;
        if (issueId.Length == 0)
        {
            return;
        }

        await _apiClient.RetryRunAsync(issueId, cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private Task OpenWorkspaceAsync(CancellationToken cancellationToken)
    {
        if (SelectedWorkspace.Length > 0)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SelectedWorkspace}\"") { UseShellExecute = true });
        }

        return Task.CompletedTask;
    }

    private Task OpenIssueAsync(CancellationToken cancellationToken)
    {
        if (SelectedIdentifier.Length > 0)
        {
            Process.Start(new ProcessStartInfo($"https://linear.app/issue/{SelectedIdentifier}") { UseShellExecute = true });
        }

        return Task.CompletedTask;
    }

    private bool HasWorkspace()
    {
        return SelectedWorkspace.Length > 0;
    }

    private void RaiseSelectionDependentCommands()
    {
        OnPropertyChanged(nameof(SelectedIdentifier));
        OnPropertyChanged(nameof(SelectedIssueId));
        OnPropertyChanged(nameof(SelectedWorkspace));
        OnPropertyChanged(nameof(SelectedLastMessage));
        StopCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        OpenWorkspaceCommand.RaiseCanExecuteChanged();
        OpenIssueCommand.RaiseCanExecuteChanged();
    }

    private void RestoreSelection(string? selectedRunIssueId, string? selectedRetryIssueId, string? selectedCompletedIssueId)
    {
        if (!string.IsNullOrWhiteSpace(selectedRunIssueId))
        {
            var run = Running.FirstOrDefault(item => item.IssueId == selectedRunIssueId);
            if (run is not null)
            {
                SelectedRun = run;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedRetryIssueId))
        {
            var retry = Retrying.FirstOrDefault(item => item.IssueId == selectedRetryIssueId);
            if (retry is not null)
            {
                SelectedRetry = retry;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedCompletedIssueId))
        {
            var completed = Completed.FirstOrDefault(item => item.IssueId == selectedCompletedIssueId);
            if (completed is not null)
            {
                SelectedCompleted = completed;
                return;
            }
        }

        if (SelectedRun is not null)
        {
            SelectedRun = null;
        }

        if (SelectedRetry is not null)
        {
            SelectedRetry = null;
        }

        if (SelectedCompleted is not null)
        {
            SelectedCompleted = null;
        }
    }

    private string SelectedCompletedSummary()
    {
        if (SelectedCompleted is null)
        {
            return "";
        }

        return string.Join(
            Environment.NewLine,
            $"Status: {SelectedCompleted.Status}",
            $"State: {SelectedCompleted.State}",
            $"Completed: {SelectedCompleted.CompletedAt:G}",
            $"Cleanup: {SelectedCompleted.CleanupOutcome}",
            $"Session: {SelectedCompleted.SessionId ?? "-"}",
            $"Thread: {SelectedCompleted.ThreadId ?? "-"}",
            $"Turns: {SelectedCompleted.TurnCount}",
            $"Tokens: {SelectedCompleted.Tokens.TotalTokens:N0}",
            $"Last event: {SelectedCompleted.LastEvent ?? "-"}",
            $"Message: {SelectedCompleted.LastMessage ?? SelectedCompleted.Error ?? "-"}");
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            Application.Current.Dispatcher.Invoke(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
