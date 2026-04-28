# Operator-First Symphony Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a WPF operator console and daemon control endpoints so the .NET Symphony implementation is easy to supervise, inspect, stop, and retry on a Windows dev machine.

**Architecture:** Keep `Symphony.Service` as the daemon and HTTP API. Add `Symphony.Operator` as a WPF MVVM client that polls the local API and invokes operator actions. Keep the UI dense and utilitarian; it should expose operational state, not become a decorative dashboard.

**Tech Stack:** .NET 10, WPF, C#, MVVM without a heavy framework, ASP.NET Core minimal APIs, `HttpClient`, `System.Text.Json`, Windows shell integration through `Process.Start`.

---

## Implementation Rules

- Do not rewrite the daemon.
- Keep the WPF app dependency-light: no Prism, ReactiveUI, or large UI framework for the first pass.
- Polling is acceptable; do not add SignalR yet.
- Keep actions explicit and reversible where possible.
- Build validation is required. Runtime smoke is strongly preferred.

## File Map

- Create `dotnet/src/Symphony.Operator/Symphony.Operator.csproj`: WPF app.
- Create `dotnet/src/Symphony.Operator/App.xaml` and `App.xaml.cs`: application startup.
- Create `dotnet/src/Symphony.Operator/MainWindow.xaml` and `MainWindow.xaml.cs`: main shell.
- Create `dotnet/src/Symphony.Operator/Commands/AsyncRelayCommand.cs`: async command helper.
- Create `dotnet/src/Symphony.Operator/Models/*.cs`: API DTOs.
- Create `dotnet/src/Symphony.Operator/Services/SymphonyApiClient.cs`: local daemon API client.
- Create `dotnet/src/Symphony.Operator/ViewModels/MainWindowViewModel.cs`: polling, selection, commands.
- Modify `dotnet/Symphony.slnx`: include the operator project.
- Modify `dotnet/src/Symphony.Service/Hosting/ManualRefreshSignal.cs` or current host state/action surface if split is useful.
- Modify `dotnet/src/Symphony.Service/Hosting/SymphonyHostedService.cs`: expose stop/retry/log action methods through a singleton control service.
- Modify `dotnet/src/Symphony.Service/Observability/HttpApi.cs`: add operator endpoints.
- Modify `dotnet/src/Symphony.Service/Hosting/RuntimeStateStore.cs`: retain recent log/event lines if needed.
- Modify `dotnet/README.md`: document the operator console.

## Task 1: Extract Daemon Control Surface

**Files:**
- Create: `dotnet/src/Symphony.Service/Hosting/DaemonControlService.cs`
- Modify: `dotnet/src/Symphony.Service/Hosting/SymphonyHostedService.cs`
- Modify: `dotnet/src/Symphony.Service/Program.cs`

- [ ] **Step 1: Add a control service**

Create `DaemonControlService` with methods:

```csharp
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

    public Task StopRunAsync(string issueId, bool cleanupWorkspace, CancellationToken cancellationToken) =>
        _stopRun?.Invoke(issueId, cleanupWorkspace, cancellationToken) ?? Task.CompletedTask;

    public Task RetryRunAsync(string issueId, CancellationToken cancellationToken) =>
        _retryRun?.Invoke(issueId, cancellationToken) ?? Task.CompletedTask;

    public Task RefreshAsync(CancellationToken cancellationToken) =>
        _refresh?.Invoke(cancellationToken) ?? Task.CompletedTask;
}
```

- [ ] **Step 2: Bind hosted service actions**

In `SymphonyHostedService`, inject `DaemonControlService` and call `Bind(...)` at startup. Implement:

- stop: cancel active CTS by issue ID; optionally remove workspace if requested and snapshot has workspace/worker data
- retry: request refresh and, if issue is already in retry queue, make it due now; if active, stop then enqueue retry
- refresh: request immediate poll

Keep the implementation crude but deterministic.

- [ ] **Step 3: Register service**

In `Program.cs`, register `DaemonControlService` as singleton before hosted services.

- [ ] **Step 4: Build**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

## Task 2: Add Operator HTTP Endpoints

**Files:**
- Modify: `dotnet/src/Symphony.Service/Observability/HttpApi.cs`
- Modify: `dotnet/src/Symphony.Service/Hosting/RuntimeStateStore.cs`

- [ ] **Step 1: Add health endpoint**

Add:

```text
GET /api/v1/health
```

Return daemon status, generated time, running count, retry count, and whether operator actions are available.

- [ ] **Step 2: Add stop endpoint**

Add:

```text
POST /api/v1/runs/{issue_id}/stop
```

Accept optional JSON:

```json
{ "cleanup_workspace": false }
```

Call `DaemonControlService.StopRunAsync(issue_id, cleanup_workspace, cancellationToken)` and return `202 Accepted`.

- [ ] **Step 3: Add retry endpoint**

Add:

```text
POST /api/v1/runs/{issue_id}/retry
```

Call `DaemonControlService.RetryRunAsync(issue_id, cancellationToken)` and return `202 Accepted`.

- [ ] **Step 4: Add recent logs endpoint**

Add:

```text
GET /api/v1/logs/recent?count=200
```

For the first pass, read recent lines from the current log file if available or return recent in-memory event summaries from `RuntimeStateStore`. Do not block UI on file tailing complexity.

- [ ] **Step 5: Build**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

## Task 3: Scaffold WPF Operator Project

**Files:**
- Create: `dotnet/src/Symphony.Operator/Symphony.Operator.csproj`
- Create: `dotnet/src/Symphony.Operator/App.xaml`
- Create: `dotnet/src/Symphony.Operator/App.xaml.cs`
- Create: `dotnet/src/Symphony.Operator/MainWindow.xaml`
- Create: `dotnet/src/Symphony.Operator/MainWindow.xaml.cs`
- Modify: `dotnet/Symphony.slnx`

- [ ] **Step 1: Create WPF project**

Run:

```powershell
dotnet new wpf -n Symphony.Operator -o dotnet/src/Symphony.Operator
dotnet sln dotnet/Symphony.slnx add dotnet/src/Symphony.Operator/Symphony.Operator.csproj
```

- [ ] **Step 2: Set project defaults**

Ensure the csproj targets `net10.0-windows`, has `<UseWPF>true</UseWPF>`, nullable enabled, and implicit usings enabled.

- [ ] **Step 3: Build**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

## Task 4: Add Operator Models and API Client

**Files:**
- Create: `dotnet/src/Symphony.Operator/Models/SymphonyStateDto.cs`
- Create: `dotnet/src/Symphony.Operator/Models/RunDto.cs`
- Create: `dotnet/src/Symphony.Operator/Models/RetryDto.cs`
- Create: `dotnet/src/Symphony.Operator/Models/HealthDto.cs`
- Create: `dotnet/src/Symphony.Operator/Services/SymphonyApiClient.cs`

- [ ] **Step 1: Add DTOs**

Model the current `/api/v1/state` JSON shape:

- generated time
- counts
- running list
- retrying list
- codex totals
- rate limits

Use `JsonPropertyName` attributes for snake_case fields.

- [ ] **Step 2: Add API client**

Implement methods:

```csharp
Task<SymphonyStateDto?> GetStateAsync(CancellationToken cancellationToken);
Task<HealthDto?> GetHealthAsync(CancellationToken cancellationToken);
Task RefreshAsync(CancellationToken cancellationToken);
Task StopRunAsync(string issueId, bool cleanupWorkspace, CancellationToken cancellationToken);
Task RetryRunAsync(string issueId, CancellationToken cancellationToken);
Task<IReadOnlyList<string>> GetRecentLogsAsync(int count, CancellationToken cancellationToken);
```

Base URL default: `http://127.0.0.1:4027`.

- [ ] **Step 3: Build**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

## Task 5: Add MVVM View Model and Commands

**Files:**
- Create: `dotnet/src/Symphony.Operator/Commands/AsyncRelayCommand.cs`
- Create: `dotnet/src/Symphony.Operator/ViewModels/MainWindowViewModel.cs`
- Modify: `dotnet/src/Symphony.Operator/App.xaml.cs`
- Modify: `dotnet/src/Symphony.Operator/MainWindow.xaml.cs`

- [ ] **Step 1: Add command helper**

Implement `AsyncRelayCommand : ICommand` with `CanExecute`, `ExecuteAsync`, and reentrancy protection.

- [ ] **Step 2: Add view model state**

`MainWindowViewModel` should expose:

- `ObservableCollection<RunDto> Running`
- `ObservableCollection<RetryDto> Retrying`
- `RunDto? SelectedRun`
- `RetryDto? SelectedRetry`
- `string ConnectionStatus`
- `string LastError`
- `string[] RecentLogs`
- token totals
- poll status
- async commands: refresh, stop, retry, open workspace, open issue

- [ ] **Step 3: Add polling loop**

Start a background polling loop from the view model that calls `GetStateAsync` every 1.5 seconds and updates collections on the dispatcher.

- [ ] **Step 4: Add shell actions**

Open workspace with `explorer.exe <path>`. Open issue URL with `ProcessStartInfo { UseShellExecute = true }`.

- [ ] **Step 5: Build**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

## Task 6: Build Dense WPF Shell

**Files:**
- Modify: `dotnet/src/Symphony.Operator/MainWindow.xaml`

- [ ] **Step 1: Create layout**

Use a three-region layout:

- top toolbar: connection, refresh, token totals, polling state
- left/main: tabs or split grids for running and retrying
- right detail panel: selected run/retry detail plus actions and log tail

- [ ] **Step 2: Add running grid**

Columns:

- identifier
- state
- worker
- session
- last event
- started
- workspace

- [ ] **Step 3: Add retry grid**

Columns:

- identifier
- attempt
- due at
- worker
- error

- [ ] **Step 4: Add detail/action panel**

Buttons:

- open workspace
- open issue
- stop
- retry now

Keep button text short. Use disabled states when no applicable selection exists.

- [ ] **Step 5: Build**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

## Task 7: Document and Smoke Validate

**Files:**
- Modify: `dotnet/README.md`

- [ ] **Step 1: Document operator console**

Add:

```powershell
dotnet run --project dotnet/src/Symphony.Service -- --i-understand-that-this-will-be-running-without-the-usual-guardrails <WORKFLOW.md> --port 4027
dotnet run --project dotnet/src/Symphony.Operator
```

Describe default API URL and what actions are available.

- [ ] **Step 2: Build full solution**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Daemon API smoke**

Run daemon with `--port 4027`, then verify:

```powershell
Invoke-RestMethod http://127.0.0.1:4027/api/v1/health
Invoke-RestMethod http://127.0.0.1:4027/api/v1/state
Invoke-RestMethod http://127.0.0.1:4027/api/v1/logs/recent
```

Expected: all return JSON without crashing the daemon.

- [ ] **Step 4: WPF launch smoke**

Run:

```powershell
dotnet run --project dotnet/src/Symphony.Operator
```

Expected: app launches, shows connection status, and displays empty state or daemon state.

