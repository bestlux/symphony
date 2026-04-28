# Operator-First Symphony Design

## Goal

Make the .NET Symphony implementation fit this Windows-first environment by turning it into an operator-supervised orchestration tool, not just a headless daemon clone of the Elixir reference.

The daemon remains the execution engine. The next phase adds a WPF operator console and the small daemon/API upgrades needed to make active runs visible, interruptible, and easy to inspect.

## Context

The current .NET implementation already has the core service spine: workflow loading, Linear polling, workspace management, Codex app-server execution, orchestration state, console dashboard, and HTTP status API.

The main gap for local use is operator confidence. Before trying to run many unattended agents, the user should be able to see what the daemon is doing, open the relevant workspace or issue, stop/retry work, and understand why a run is stalled or failing.

## Product Shape

Add `Symphony.Operator`, a WPF desktop app that connects to a running local Symphony daemon over HTTP.

The first version should be a compact operational console:

- connection status to the daemon
- active runs grid
- retry queue grid
- selected issue/run details
- polling status and token totals
- last Codex event and message
- open workspace button
- open Linear issue button
- refresh now button
- stop run button
- retry now button
- tail recent daemon log lines

The UI should be quiet, dense, and utilitarian. It should feel like a developer operations tool, not a marketing dashboard.

## Daemon/API Additions

The existing HTTP API needs a few operator controls beyond read-only state:

- `POST /api/v1/runs/{issue_id}/stop`
- `POST /api/v1/runs/{issue_id}/retry`
- `GET /api/v1/logs/recent`
- optional `GET /api/v1/health`

Stop should cancel the active run without deleting the workspace unless the request explicitly asks for cleanup. Retry should enqueue or immediately dispatch the issue if there is worker capacity.

The daemon should also expose enough fields for the operator UI to avoid guessing:

- issue URL
- workspace path
- worker host
- run started time
- last event time
- retry due time
- last error
- whether an action is currently allowed

## WPF Architecture

Use MVVM with explicit async UI state.

Suggested project layout:

- `Symphony.Operator`: WPF app
- `Services/SymphonyApiClient.cs`: HTTP API client
- `Models/*`: DTOs matching API payloads
- `ViewModels/MainWindowViewModel.cs`: polling, selection, commands
- `Views/MainWindow.xaml`: main shell
- `Commands/AsyncRelayCommand.cs`: small command helper

The UI should poll `/api/v1/state` every 1-2 seconds. SignalR is unnecessary for the first version; polling is simpler and good enough locally.

## Done When

This phase is done when a developer can:

1. start the daemon with `--port`
2. launch the WPF operator console
3. see active runs and retries
4. select a run and inspect issue/workspace/event/log context
5. trigger refresh, stop, and retry actions
6. open the workspace and issue URL from the UI
7. build the full solution successfully

