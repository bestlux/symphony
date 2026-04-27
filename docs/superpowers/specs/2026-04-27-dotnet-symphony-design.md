# .NET Symphony Implementation Design

## Goal

Build a native .NET implementation of Symphony that conforms to the repository `SPEC.md` core contract while staying practical for Windows-first local operation.

The first implementation slice is a headless daemon and local observability API. A WPF operator UI is intentionally deferred until the daemon has stable state, logs, and API contracts to observe.

## Context

The existing repository contains a language-agnostic Symphony service specification and an Elixir/OTP reference implementation. Symphony polls Linear for eligible work, creates isolated per-issue workspaces, runs Codex in app-server mode inside those workspaces, retries or stops runs based on tracker state, and exposes enough observability for operators to understand active work.

The .NET implementation should reuse the specification as the source of truth rather than copy Elixir-specific structure. It should be able to run without a desktop session, but should leave clean seams for a future WPF dashboard.

## Constraints

- Keep the daemon UI-agnostic.
- Use .NET patterns that fit long-running services: Generic Host, hosted services, cancellation tokens, typed options, dependency injection, and structured logging.
- Keep workspace safety explicit: Codex must run only inside the per-issue workspace, and workspace paths must remain under the configured root.
- Start with Linear as the tracker adapter because `SPEC.md` v1 defines Linear-compatible behavior.
- Start with local subprocess Codex app-server execution. SSH workers, persisted retry queues, and WPF are follow-on slices.
- Preserve compatibility with the root `SPEC.md` where practical. If behavior intentionally diverges, document it.

## Recommended Architecture

Create a new `dotnet/` implementation directory with a solution split into focused projects:

- `Symphony.Core`: domain models, workflow parsing contracts, config contracts, prompt rendering contracts, orchestrator abstractions.
- `Symphony.Service`: Generic Host entrypoint, CLI arguments, hosted orchestration loop, local HTTP API.
- `Symphony.Linear`: Linear GraphQL client and tracker adapter.
- `Symphony.Workspaces`: workspace path mapping, directory lifecycle, hook execution, path safety.
- `Symphony.Codex`: Codex app-server subprocess launcher and protocol client.
- `Symphony.Tests`: focused unit tests for parser/config/workspace/orchestrator behavior.

The daemon should start as:

```powershell
dotnet run --project dotnet/src/Symphony.Service -- ./WORKFLOW.md --port 4027
```

The implementation should not include WPF in the first slice. The local HTTP API should make a future WPF client straightforward.

## First Slice Behavior

The first usable milestone should support:

- Load `WORKFLOW.md` from an explicit path or default path.
- Parse YAML front matter and Markdown prompt body.
- Resolve core config defaults.
- Validate required Linear/workspace/Codex settings before dispatch.
- Poll Linear for active candidate issues.
- Dispatch up to `agent.max_concurrent_agents`.
- Create or reuse a deterministic workspace per issue identifier.
- Run `hooks.after_create` only for newly created workspaces.
- Launch `codex app-server` from the issue workspace.
- Render and send the initial prompt to Codex.
- Track active sessions, retry entries, token counters where available, and last observed Codex event.
- Stop/release active runs when issues become terminal or non-active.
- Retry failures with exponential backoff capped by `agent.max_retry_backoff_ms`.
- Expose `/api/v1/state` and `/api/v1/refresh` on loopback when a port is configured.
- Emit structured logs to console and a log directory.

## Deferred Slices

These are intentionally out of scope for the first implementation plan:

- WPF dashboard.
- SSH worker execution.
- Persisted retry queue/session database.
- Full dashboard HTML.
- Rich Linear write automation beyond the app-server tool boundary.
- Multi-tracker support.
- Windows Service installer.

## Testing Strategy

Start with unit tests around deterministic behavior:

- Workflow parsing accepts valid front matter and rejects non-map YAML.
- Config defaults and environment-variable resolution match the selected implementation contract.
- Workspace keys are sanitized and remain under the configured root.
- Hook execution runs only on first create.
- Orchestrator dispatch respects claimed issues, active states, terminal states, and concurrency.
- Retry delays use the expected exponential backoff and cap.
- Prompt rendering fails on unknown variables.

Integration tests should be staged later:

- Fake tracker + fake Codex process for local end-to-end orchestration.
- Optional real Linear + real Codex profile gated by environment variables.

## Done When

The first slice is done when a developer can run the .NET daemon against a workflow file, see it validate configuration, poll a tracker adapter, create isolated workspaces, execute a Codex app-server run, expose runtime state through the local API, and pass the focused automated test suite.

