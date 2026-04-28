# Full .NET Symphony Implementation Design

## Goal

Build a full .NET implementation of Symphony that aims for feature parity with the Elixir reference implementation, using the repository `SPEC.md`, the OpenAI Symphony post, and the existing `elixir/` source as the behavioral target.

This is an implementation-first port. The code should stay clean and decomposed, but it should not be more cautious or elaborate than the Elixir reference. The first completed result should be a working .NET Symphony daemon, not a narrow proof of concept.

## Behavioral Target

The target system treats Linear as the work control plane. It continuously polls eligible issues, maps each issue to an isolated workspace, runs Codex app-server sessions inside those workspaces, restarts stalled or failed work, and exposes enough state for operators to see what is running.

The .NET implementation should include:

- Workflow loading and dynamic reload from `WORKFLOW.md`.
- Config defaults matching the Elixir reference where practical.
- Linear tracker adapter, including candidate issue lookup, issue-state refresh, terminal-state cleanup lookup, raw GraphQL dynamic tool support, comment creation, and state updates.
- In-memory orchestrator state with claimed issues, running sessions, completed issues, retry queue, continuation retries, and Codex token/rate-limit aggregation.
- Local workspace creation/reuse/removal, all configured workspace hooks, and SSH worker workspace operations.
- Local and SSH Codex app-server execution.
- Multi-turn agent runner with `agent.max_turns` continuation behavior.
- Codex app-server JSON-RPC-style stdio client, dynamic tool handling, approval auto-response behavior equivalent to the Elixir implementation, and non-interactive user-input handling.
- Terminal status dashboard.
- Optional loopback HTTP dashboard/API equivalent to the Elixir Phoenix surface.
- CLI guardrail acknowledgement, workflow path, `--logs-root`, and `--port`.

## Architecture

Create a new `dotnet/` solution with focused projects:

- `Symphony.Abstractions`: stable contracts and domain records shared across projects.
- `Symphony.Core`: workflow parsing, config resolution, prompt rendering, orchestration state machine, retry logic, snapshots, and log event models.
- `Symphony.Linear`: Linear GraphQL client and tracker adapter.
- `Symphony.Workspaces`: local/remote workspace lifecycle, hook execution, path helpers, SSH helpers.
- `Symphony.Codex`: app-server subprocess/SSH launcher, protocol client, dynamic tool dispatcher, approval/user-input handlers, token/rate-limit event extraction.
- `Symphony.Service`: CLI entrypoint, Generic Host wiring, hosted orchestrator service, console dashboard, HTTP dashboard/API.

The daemon should run as:

```powershell
dotnet run --project dotnet/src/Symphony.Service -- --i-understand-that-this-will-be-running-without-the-usual-guardrails ./WORKFLOW.md --port 4027
```

The implementation should use .NET 10, Microsoft.Extensions.Hosting, System.CommandLine or simple manual argument parsing, ASP.NET Core minimal APIs, built-in logging, YamlDotNet, Scriban, and System.Text.Json.

## Trust Posture

Match the spirit of the Elixir reference: this is an engineering preview for trusted environments. Keep the explicit CLI acknowledgement. Default to the same Codex safety-related config values where practical, but do not turn the implementation into a hardened sandbox project.

Workspace path checks should remain present because they are core correctness, but the implementation can be pragmatic about shell hooks, SSH commands, and dashboard polish.

## Done When

The plan is complete when `dotnet/` contains a runnable Symphony daemon that can load an Elixir-style workflow file, poll Linear, dispatch local or SSH Codex app-server workers, continue/retry/reconcile sessions, expose console and HTTP observability, inject the `linear_graphql` dynamic tool, and build successfully.

Automated tests are not required for this pass. The validation bar is compile success plus a smoke run that reaches startup/config validation without crashing.

