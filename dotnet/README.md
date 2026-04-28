# Symphony .NET

This directory contains a .NET 10 implementation of Symphony based on the root `SPEC.md` and the Elixir reference implementation.

> [!WARNING]
> This is an engineering preview for trusted environments. It launches Codex app-server workers from issue data and workflow hooks.

## Run

```powershell
dotnet run --project dotnet/src/Symphony.Service -- --i-understand-that-this-will-be-running-without-the-usual-guardrails ./WORKFLOW.md --port 4027
```

If no workflow path is provided, the service uses `./WORKFLOW.md`.

Options:

- `--logs-root <path>` writes file logs under a custom directory.
- `--port <port>` enables the loopback HTTP status API and simple dashboard.
- `--secrets <path>` loads dotenv-style secrets before reading workflow config.
- `--i-understand-that-this-will-be-running-without-the-usual-guardrails` is required.

Secrets:

```powershell
Copy-Item .\symphony.secrets.example .\symphony.secrets
notepad .\symphony.secrets
```

The service automatically loads `symphony.secrets` from the workflow file directory, or from the
current directory. Explicit environment variables still win over file values.

## Workflow

The service reads the same YAML-front-matter plus Markdown prompt format as the Elixir implementation. Common settings:

- `tracker.api_key` or `LINEAR_API_KEY`
- `tracker.project_slug`
- `workspace.root`
- `worker.ssh_hosts`
- `hooks.after_create`, `hooks.before_run`, `hooks.after_run`, `hooks.before_remove`
- `agent.max_concurrent_agents`
- `agent.max_turns`
- `codex.command`

## Observability

When a port is enabled:

- `GET /`
- `GET /api/v1/state`
- `GET /api/v1/{issue_identifier}`
- `POST /api/v1/refresh`

The console dashboard renders active runs, retry state, polling status, and token totals.

## Operator Console

Start the daemon with a loopback API:

```powershell
dotnet run --project dotnet/src/Symphony.Service -- --i-understand-that-this-will-be-running-without-the-usual-guardrails <WORKFLOW.md> --port 4027
```

Then launch the Windows operator console:

```powershell
dotnet run --project dotnet/src/Symphony.Operator
```

The operator console connects to `http://127.0.0.1:4027` and shows active runs, retry queue entries,
polling status, token totals, recent daemon events, and selected-run details. It can request a
refresh, stop an active run, retry a selected run or retry entry, open a local workspace in Explorer,
and open the issue identifier in Linear.

Additional operator API endpoints:

- `GET /api/v1/health`
- `POST /api/v1/runs/{issue_id}/stop`
- `POST /api/v1/runs/{issue_id}/retry`
- `GET /api/v1/logs/recent`

## Build

```powershell
dotnet build dotnet/Symphony.slnx
```
