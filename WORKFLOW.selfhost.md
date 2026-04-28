---
tracker:
  kind: linear
  api_key: $LINEAR_API_KEY
  project_slug: "symphony-net-e7a506c9659d"
  active_states:
    - Todo
    - In Progress
  terminal_states:
    - Done
    - Canceled
    - Duplicate
polling:
  interval_ms: 5000
workspace:
  root: D:/Source/symphony-workspaces
hooks:
  timeout_ms: 300000
  after_create: |
    $source = "D:\Source\symphony"
    git clone $source .
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    robocopy $source . /E /XD .git .vs bin obj log /XF symphony.secrets *.user *.suo
    if ($LASTEXITCODE -gt 7) { exit $LASTEXITCODE }

    $config = "C:\Users\iomancer\.codex\config.toml"
    $workspace = (Get-Location).Path
    $header = "[projects.'$($workspace.ToLowerInvariant())']"
    $trustBlock = "`n$header`ntrust_level = `"trusted`"`n"
    if (-not (Select-String -LiteralPath $config -SimpleMatch $header -Quiet)) {
      Add-Content -LiteralPath $config -Value $trustBlock
    }
    exit 0
agent:
  max_concurrent_agents: 1
  max_turns: 12
codex:
  command: codex --config shell_environment_policy.inherit=all --config model_reasoning_effort=medium --model gpt-5.5 app-server
  approval_policy: never
  thread_sandbox: workspace-write
  turn_sandbox_policy:
    type: workspaceWrite
---

You are working on Linear issue {{ issue.identifier }} for the local .NET/WPF Symphony implementation.

Repo target: D:\Source\symphony
Workspace: this isolated per-issue copy, not the daemon checkout.

Issue context:
Identifier: {{ issue.identifier }}
Title: {{ issue.title }}
Current status: {{ issue.state }}
Labels: {{ issue.labels }}
URL: {{ issue.url }}

Description:
{% if issue.description %}
{{ issue.description }}
{% else %}
No description provided.
{% endif %}

Operating rules:

1. Work only inside this workspace.
2. Do not modify `D:\Source\symphony` directly.
3. Keep changes scoped to the Linear issue.
4. Prefer direct, useful implementation over heavy process.
5. Use the existing .NET solution under `dotnet/Symphony.slnx`.
6. Validate with `dotnet build dotnet/Symphony.slnx` at minimum.
7. If you discover out-of-scope useful work, create or propose a separate Linear follow-up issue instead of expanding scope.
8. Final response must summarize changes, validation, blockers, and any follow-up issues.

Environment note:

- The workspace root `D:\Source\symphony-workspaces` should be trusted in Codex.
- The `after_create` hook also adds the exact generated workspace path to `C:\Users\iomancer\.codex\config.toml` before `codex app-server` starts.
