# Full .NET Symphony Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a full .NET Symphony implementation under `dotnet/` with Elixir-reference behavior: Linear polling, local/SSH workspaces, Codex app-server orchestration, retries, dynamic tools, terminal dashboard, and loopback HTTP observability.

**Architecture:** Use a .NET 10 Generic Host daemon with small projects by responsibility. The orchestrator owns in-memory state and scheduling; tracker, workspace, Codex, dashboard, and HTTP API implementations hang off clear interfaces. Elixir source is the reference for behavior and defaults, while `SPEC.md` remains the contract for semantics.

**Tech Stack:** .NET 10, C#, Microsoft.Extensions.Hosting, ASP.NET Core minimal APIs, System.Text.Json, YamlDotNet, Scriban, built-in logging, OpenSSH via subprocess, Codex app-server over JSON-line stdio.

---

## Implementation Rules

- Keep this crude but coherent: favor working parity over perfect abstractions.
- Do not add a WPF project yet.
- Do not create an automated test suite in this pass.
- Keep public types and methods named plainly; avoid clever generic frameworks.
- Match Elixir config defaults and behavior unless .NET requires a small practical adjustment.
- Build after major groups with `dotnet build dotnet/Symphony.slnx`.
- Final validation is build success plus a startup smoke command that proves CLI/config paths wire together.

## File Map

- Create `dotnet/Symphony.slnx`: solution.
- Create `dotnet/src/Symphony.Abstractions/Symphony.Abstractions.csproj`: shared contracts and records.
- Create `dotnet/src/Symphony.Core/Symphony.Core.csproj`: workflow/config/prompt/orchestrator state.
- Create `dotnet/src/Symphony.Linear/Symphony.Linear.csproj`: Linear GraphQL client and adapter.
- Create `dotnet/src/Symphony.Workspaces/Symphony.Workspaces.csproj`: local and SSH workspace lifecycle.
- Create `dotnet/src/Symphony.Codex/Symphony.Codex.csproj`: Codex app-server client and dynamic tool support.
- Create `dotnet/src/Symphony.Service/Symphony.Service.csproj`: CLI, hosted service, dashboard, HTTP API.
- Create `dotnet/README.md`: .NET implementation usage notes.

## Task 1: Scaffold Solution and Projects

**Files:**
- Create: `dotnet/Symphony.slnx`
- Create: `dotnet/src/Symphony.Abstractions/Symphony.Abstractions.csproj`
- Create: `dotnet/src/Symphony.Core/Symphony.Core.csproj`
- Create: `dotnet/src/Symphony.Linear/Symphony.Linear.csproj`
- Create: `dotnet/src/Symphony.Workspaces/Symphony.Workspaces.csproj`
- Create: `dotnet/src/Symphony.Codex/Symphony.Codex.csproj`
- Create: `dotnet/src/Symphony.Service/Symphony.Service.csproj`

- [ ] **Step 1: Create the solution and class libraries**

Run:

```powershell
New-Item -ItemType Directory -Force -Path dotnet/src | Out-Null
dotnet new sln -n Symphony -o dotnet
dotnet new classlib -n Symphony.Abstractions -o dotnet/src/Symphony.Abstractions
dotnet new classlib -n Symphony.Core -o dotnet/src/Symphony.Core
dotnet new classlib -n Symphony.Linear -o dotnet/src/Symphony.Linear
dotnet new classlib -n Symphony.Workspaces -o dotnet/src/Symphony.Workspaces
dotnet new classlib -n Symphony.Codex -o dotnet/src/Symphony.Codex
dotnet new web -n Symphony.Service -o dotnet/src/Symphony.Service
```

- [ ] **Step 2: Add projects to the solution**

Run:

```powershell
dotnet sln dotnet/Symphony.slnx add dotnet/src/Symphony.Abstractions/Symphony.Abstractions.csproj
dotnet sln dotnet/Symphony.slnx add dotnet/src/Symphony.Core/Symphony.Core.csproj
dotnet sln dotnet/Symphony.slnx add dotnet/src/Symphony.Linear/Symphony.Linear.csproj
dotnet sln dotnet/Symphony.slnx add dotnet/src/Symphony.Workspaces/Symphony.Workspaces.csproj
dotnet sln dotnet/Symphony.slnx add dotnet/src/Symphony.Codex/Symphony.Codex.csproj
dotnet sln dotnet/Symphony.slnx add dotnet/src/Symphony.Service/Symphony.Service.csproj
```

- [ ] **Step 3: Add references**

Run:

```powershell
dotnet add dotnet/src/Symphony.Core/Symphony.Core.csproj reference dotnet/src/Symphony.Abstractions/Symphony.Abstractions.csproj
dotnet add dotnet/src/Symphony.Linear/Symphony.Linear.csproj reference dotnet/src/Symphony.Abstractions/Symphony.Abstractions.csproj dotnet/src/Symphony.Core/Symphony.Core.csproj
dotnet add dotnet/src/Symphony.Workspaces/Symphony.Workspaces.csproj reference dotnet/src/Symphony.Abstractions/Symphony.Abstractions.csproj dotnet/src/Symphony.Core/Symphony.Core.csproj
dotnet add dotnet/src/Symphony.Codex/Symphony.Codex.csproj reference dotnet/src/Symphony.Abstractions/Symphony.Abstractions.csproj dotnet/src/Symphony.Core/Symphony.Core.csproj dotnet/src/Symphony.Linear/Symphony.Linear.csproj
dotnet add dotnet/src/Symphony.Service/Symphony.Service.csproj reference dotnet/src/Symphony.Abstractions/Symphony.Abstractions.csproj dotnet/src/Symphony.Core/Symphony.Core.csproj dotnet/src/Symphony.Linear/Symphony.Linear.csproj dotnet/src/Symphony.Workspaces/Symphony.Workspaces.csproj dotnet/src/Symphony.Codex/Symphony.Codex.csproj
```

- [ ] **Step 4: Add packages**

Run:

```powershell
dotnet add dotnet/src/Symphony.Core/Symphony.Core.csproj package YamlDotNet
dotnet add dotnet/src/Symphony.Core/Symphony.Core.csproj package Scriban
dotnet add dotnet/src/Symphony.Service/Symphony.Service.csproj package Microsoft.Extensions.Hosting
```

- [ ] **Step 5: Build the empty scaffold**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

## Task 2: Define Shared Domain Contracts

**Files:**
- Create: `dotnet/src/Symphony.Abstractions/Issues/Issue.cs`
- Create: `dotnet/src/Symphony.Abstractions/Issues/BlockerRef.cs`
- Create: `dotnet/src/Symphony.Abstractions/Tracking/ITrackerClient.cs`
- Create: `dotnet/src/Symphony.Abstractions/Workspaces/WorkspaceInfo.cs`
- Create: `dotnet/src/Symphony.Abstractions/Runtime/CodexRuntimeUpdate.cs`
- Create: `dotnet/src/Symphony.Abstractions/Runtime/RunResult.cs`
- Create: `dotnet/src/Symphony.Abstractions/Runtime/RuntimeSnapshot.cs`

- [ ] **Step 1: Add issue models**

Create `Issue` with fields matching `SPEC.md`: `Id`, `Identifier`, `Title`, `Description`, `Priority`, `State`, `BranchName`, `Url`, `Labels`, `BlockedBy`, `CreatedAt`, `UpdatedAt`, `AssigneeId`.

- [ ] **Step 2: Add tracker interface**

`ITrackerClient` should expose:

```csharp
Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken);
Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(IReadOnlyList<string> states, CancellationToken cancellationToken);
Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(IReadOnlyList<string> issueIds, CancellationToken cancellationToken);
Task<JsonDocument> GraphQlAsync(string query, JsonObject variables, CancellationToken cancellationToken);
Task CreateCommentAsync(string issueId, string body, CancellationToken cancellationToken);
Task UpdateIssueStateAsync(string issueId, string stateName, CancellationToken cancellationToken);
```

- [ ] **Step 3: Add runtime models**

Add records for running sessions, retry entries, Codex totals, rate limits, polling status, and snapshot payloads. Keep them serializable with `System.Text.Json`.

- [ ] **Step 4: Build**

Run `dotnet build dotnet/Symphony.slnx`.

Expected: build succeeds.

## Task 3: Implement Workflow, Config, and Prompt Rendering

**Files:**
- Create: `dotnet/src/Symphony.Core/Workflow/WorkflowDefinition.cs`
- Create: `dotnet/src/Symphony.Core/Workflow/WorkflowLoader.cs`
- Create: `dotnet/src/Symphony.Core/Workflow/WorkflowStore.cs`
- Create: `dotnet/src/Symphony.Core/Configuration/SymphonyConfig.cs`
- Create: `dotnet/src/Symphony.Core/Configuration/ConfigResolver.cs`
- Create: `dotnet/src/Symphony.Core/Prompts/PromptRenderer.cs`

- [ ] **Step 1: Implement front matter parsing**

`WorkflowLoader` reads a file, extracts YAML between leading `---` markers, and returns `{config, promptTemplate}`. Reject missing files, invalid YAML, and non-map YAML with explicit exceptions/messages.

- [ ] **Step 2: Implement config records and defaults**

Mirror Elixir defaults:

```text
tracker.endpoint = https://api.linear.app/graphql
tracker.active_states = Todo, In Progress
tracker.terminal_states = Closed, Cancelled, Canceled, Duplicate, Done
polling.interval_ms = 30000
workspace.root = %TEMP%/symphony_workspaces
agent.max_concurrent_agents = 10
agent.max_turns = 20
agent.max_retry_backoff_ms = 300000
codex.command = codex app-server
codex.approval_policy = reject sandbox/rules/mcp elicitations
codex.thread_sandbox = workspace-write
codex.turn_timeout_ms = 3600000
codex.read_timeout_ms = 5000
codex.stall_timeout_ms = 300000
hooks.timeout_ms = 60000
server.host = 127.0.0.1
observability.dashboard_enabled = true
```

- [ ] **Step 3: Implement environment and path resolution**

Resolve `tracker.api_key` from `LINEAR_API_KEY` when unset or `$LINEAR_API_KEY`. Resolve `tracker.assignee` from `LINEAR_ASSIGNEE`. Expand `~`, `$VAR`, and `%VAR%` in workspace paths.

- [ ] **Step 4: Implement prompt rendering**

Use Scriban with strict variable behavior. Provide `issue` and optional `attempt`. Use the Elixir default prompt when body is blank.

- [ ] **Step 5: Add dynamic reload support**

`WorkflowStore` keeps the last good workflow/config, watches `LastWriteTimeUtc`, reloads before polls and dispatches, and logs invalid reloads while retaining the previous good config.

- [ ] **Step 6: Build**

Run `dotnet build dotnet/Symphony.slnx`.

Expected: build succeeds.

## Task 4: Implement Linear Adapter and Dynamic GraphQL Access

**Files:**
- Create: `dotnet/src/Symphony.Linear/LinearClient.cs`
- Create: `dotnet/src/Symphony.Linear/LinearTrackerClient.cs`
- Create: `dotnet/src/Symphony.Linear/LinearQueries.cs`
- Create: `dotnet/src/Symphony.Linear/LinearIssueNormalizer.cs`

- [ ] **Step 1: Implement GraphQL HTTP client**

Use `HttpClient` with `Authorization: <api-key>` and JSON body `{ "query": "...", "variables": { ... } }`. Return parsed `JsonDocument` and throw useful exceptions for non-2xx responses.

- [ ] **Step 2: Port candidate issue query**

Implement candidate fetch by project slug, active states, optional assignee, blocked dependency filtering, priority ordering, and normalized issue model output.

- [ ] **Step 3: Port issue-state lookup**

Implement `FetchIssueStatesByIdsAsync` and `FetchIssuesByStatesAsync` for reconciliation and startup terminal cleanup.

- [ ] **Step 4: Add mutation helpers**

Implement comment creation and update-state using the same GraphQL mutations as Elixir, including state ID resolution from the issue team.

- [ ] **Step 5: Build**

Run `dotnet build dotnet/Symphony.slnx`.

Expected: build succeeds.

## Task 5: Implement Workspace and SSH Support

**Files:**
- Create: `dotnet/src/Symphony.Workspaces/WorkspaceManager.cs`
- Create: `dotnet/src/Symphony.Workspaces/PathSafety.cs`
- Create: `dotnet/src/Symphony.Workspaces/HookRunner.cs`
- Create: `dotnet/src/Symphony.Workspaces/SshClient.cs`

- [ ] **Step 1: Implement safe workspace identifiers**

Replace non `[a-zA-Z0-9._-]` characters with `_`, default missing identifiers to `issue`, and map to `workspace.root/<safe-id>`.

- [ ] **Step 2: Implement local workspace lifecycle**

Create directories, remove non-directory collisions before creation, reuse existing directories, and run `hooks.after_create` only when newly created.

- [ ] **Step 3: Implement hook execution**

Run local hooks through shell. On Windows use `powershell -NoProfile -ExecutionPolicy Bypass -Command`; when `bash` exists and the command looks POSIX-oriented, allow `bash -lc`. For crude parity, prefer working over perfect shell inference. Enforce `hooks.timeout_ms`.

- [ ] **Step 4: Implement SSH helper**

Run `ssh -T [-F $SYMPHONY_SSH_CONFIG] [-p port] destination "bash -lc '<command>'"`. Support `host:port` shorthand like Elixir.

- [ ] **Step 5: Implement remote workspace lifecycle**

Over SSH, create/reuse/remove workspace directories and run hooks remotely with `cd <workspace> && <hook>`.

- [ ] **Step 6: Build**

Run `dotnet build dotnet/Symphony.slnx`.

Expected: build succeeds.

## Task 6: Implement Codex App-Server Client

**Files:**
- Create: `dotnet/src/Symphony.Codex/CodexAppServerClient.cs`
- Create: `dotnet/src/Symphony.Codex/CodexProcess.cs`
- Create: `dotnet/src/Symphony.Codex/CodexProtocol.cs`
- Create: `dotnet/src/Symphony.Codex/DynamicToolDispatcher.cs`
- Create: `dotnet/src/Symphony.Codex/ApprovalHandler.cs`
- Create: `dotnet/src/Symphony.Codex/CodexEventMapper.cs`

- [ ] **Step 1: Launch local app-server**

Start the configured `codex.command` in the issue workspace, redirect stdin/stdout/stderr, and read newline-delimited JSON. Use shell execution for command strings to match Elixir pragmatism.

- [ ] **Step 2: Launch remote app-server**

For SSH workers, execute `cd <workspace> && exec <codex.command>` through the SSH helper and wire the process streams the same way.

- [ ] **Step 3: Implement initialize/thread/start/turn/start**

Send:

```json
{"method":"initialize","id":1,"params":{"capabilities":{"experimentalApi":true},"clientInfo":{"name":"symphony-orchestrator","title":"Symphony Orchestrator","version":"0.1.0"}}}
```

Then `initialized`, then `thread/start` with approval policy, sandbox, cwd, and dynamic tools, then `turn/start` with thread ID, prompt text, title, approval policy, cwd, and sandbox policy.

- [ ] **Step 4: Implement receive loop**

Read app-server messages until turn completion, timeout, process exit, approval-required failure, or protocol error. Emit runtime updates to the orchestrator for every meaningful event.

- [ ] **Step 5: Implement `linear_graphql` dynamic tool**

Advertise the tool spec and execute requests through `ITrackerClient.GraphQlAsync`. Return the same success/output/contentItems shape as Elixir.

- [ ] **Step 6: Implement approval and user-input behavior**

When `approval_policy` is `never`, auto-approve supported app-server approval requests. For user-input requests, return the non-interactive answer text equivalent to Elixir. Unsupported approvals should fail the turn instead of waiting forever.

- [ ] **Step 7: Extract token/rate-limit data**

Map app-server update payloads into `CodexRuntimeUpdate`: session IDs, thread IDs, turn IDs, last event, last message summary, token totals, and rate-limit snapshots where present.

- [ ] **Step 8: Build**

Run `dotnet build dotnet/Symphony.slnx`.

Expected: build succeeds.

## Task 7: Implement Agent Runner

**Files:**
- Create: `dotnet/src/Symphony.Core/Agents/AgentRunner.cs`
- Create: `dotnet/src/Symphony.Core/Agents/IAgentRunner.cs`

- [ ] **Step 1: Create workspace and emit runtime info**

For each issue, select local or preferred SSH worker, create/reuse workspace, and notify orchestrator of `worker_host` and `workspace_path`.

- [ ] **Step 2: Run hooks and Codex**

Run `before_run`, start one app-server session, execute turns, then always run `after_run` best-effort.

- [ ] **Step 3: Implement multi-turn continuation**

Turn 1 uses rendered workflow prompt. Turns 2..`agent.max_turns` use concise continuation guidance. After each successful turn, refresh issue state; continue only while issue remains active and routable.

- [ ] **Step 4: Build**

Run `dotnet build dotnet/Symphony.slnx`.

Expected: build succeeds.

## Task 8: Implement Orchestrator

**Files:**
- Create: `dotnet/src/Symphony.Core/Orchestration/SymphonyOrchestrator.cs`
- Create: `dotnet/src/Symphony.Core/Orchestration/OrchestratorState.cs`
- Create: `dotnet/src/Symphony.Core/Orchestration/RetryScheduler.cs`
- Create: `dotnet/src/Symphony.Core/Orchestration/WorkerHostSelector.cs`

- [ ] **Step 1: Implement state**

Track poll interval, next poll due, poll in progress, running map, completed set, claimed set, retry attempts, Codex totals, rate limits, and cancellation handles for workers/retries.

- [ ] **Step 2: Implement startup cleanup**

On startup, fetch terminal-state issues and remove matching workspaces across local or configured SSH worker hosts.

- [ ] **Step 3: Implement poll cycle**

On each tick: reload config, reconcile running issues, validate config, fetch candidate issues, sort by priority/created/identifier, dispatch until global/state/worker capacity is exhausted, schedule next tick.

- [ ] **Step 4: Implement dispatch**

Before dispatch, re-fetch the specific issue state. Skip terminal, non-active, blocked, or no-longer-routable issues. Claim the issue, select worker host, start agent task, and store running metadata.

- [ ] **Step 5: Implement completion and retries**

Normal worker exit schedules a continuation retry after 1000 ms. Failure exit schedules exponential backoff from 10000 ms capped by `agent.max_retry_backoff_ms`. Retry state preserves identifier, worker host, workspace, and error.

- [ ] **Step 6: Implement reconciliation**

Refresh running issue states. Stop active workers when issues become terminal, non-active, missing, blocked, or unroutable. Clean up terminal workspaces. Detect stalls using `codex.stall_timeout_ms` and restart with backoff.

- [ ] **Step 7: Implement snapshot and manual refresh**

Expose a thread-safe snapshot method and `RequestRefresh()` trigger for HTTP/dashboard consumers.

- [ ] **Step 8: Build**

Run `dotnet build dotnet/Symphony.slnx`.

Expected: build succeeds.

## Task 9: Implement Service Host, CLI, Logging, Dashboard, and HTTP API

**Files:**
- Replace: `dotnet/src/Symphony.Service/Program.cs`
- Create: `dotnet/src/Symphony.Service/Cli/CliOptions.cs`
- Create: `dotnet/src/Symphony.Service/Cli/CliParser.cs`
- Create: `dotnet/src/Symphony.Service/Hosting/SymphonyHostedService.cs`
- Create: `dotnet/src/Symphony.Service/Observability/ConsoleDashboard.cs`
- Create: `dotnet/src/Symphony.Service/Observability/HttpApi.cs`
- Create: `dotnet/src/Symphony.Service/Logging/LogFileWriterProvider.cs`

- [ ] **Step 1: Implement CLI**

Support:

```text
symphony [--logs-root <path>] [--port <port>] [path-to-WORKFLOW.md] --i-understand-that-this-will-be-running-without-the-usual-guardrails
```

Reject startup without the acknowledgement, matching Elixir’s preview posture.

- [ ] **Step 2: Wire Generic Host**

Register workflow store, config resolver, Linear client, workspace manager, Codex client, agent runner, orchestrator, hosted service, dashboard, and optional HTTP API.

- [ ] **Step 3: Implement terminal dashboard**

Render running sessions, retry queue, polling status, token totals, rate limits, last Codex event/message, worker host, workspace path, and runtime. Keep it simple and redraw every second.

- [ ] **Step 4: Implement HTTP API**

When `--port` or `server.port` is configured, bind loopback by default and expose:

```text
GET  /
GET  /api/v1/state
GET  /api/v1/{issue_identifier}
POST /api/v1/refresh
```

The root can be crude server-rendered HTML or plain text. JSON shape should mirror Elixir presenter closely enough for a future WPF or web client.

- [ ] **Step 5: Implement log file output**

Write console logs and a file log under `--logs-root` or default `./log`. Include issue ID, identifier, session ID, worker host, workspace, and event names where available.

- [ ] **Step 6: Build**

Run `dotnet build dotnet/Symphony.slnx`.

Expected: build succeeds.

## Task 10: Documentation and Smoke Validation

**Files:**
- Create: `dotnet/README.md`
- Modify: `README.md`

- [ ] **Step 1: Document .NET usage**

Document prerequisites, workflow file, env vars, local run command, SSH worker requirements, unsafe acknowledgement flag, logs, and HTTP API.

- [ ] **Step 2: Link from root README**

Add a third option or note under “Make your own” pointing to `dotnet/README.md` as the local .NET implementation.

- [ ] **Step 3: Full build**

Run:

```powershell
dotnet build dotnet/Symphony.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Startup smoke**

Run against the reference workflow without Linear auth if no token is available:

```powershell
dotnet run --project dotnet/src/Symphony.Service -- --i-understand-that-this-will-be-running-without-the-usual-guardrails elixir/WORKFLOW.md --port 4027
```

Expected: service starts, loads workflow, exposes startup logs, and reports missing Linear auth or begins polling if `LINEAR_API_KEY` is set. Stop it after confirming startup.

- [ ] **Step 5: Final status**

Record build command, smoke command, whether Linear auth was present, and any known gaps in the final handoff.

