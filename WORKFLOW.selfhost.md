---
tracker:
  kind: linear
  api_key: $LINEAR_API_KEY
  project_slug: "symphony-net-e7a506c9659d"
  active_states:
    - Todo
    - Running
    - Ready for Review
    - Reviewing
  dispatch_states:
    - Todo
    - Ready for Review
    - Reviewing
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
    $baseBranch = if ([string]::IsNullOrWhiteSpace($env:SYMPHONY_BASE_BRANCH)) { "main" } else { $env:SYMPHONY_BASE_BRANCH }

    git clone --no-local --branch $baseBranch --single-branch $source .
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git fetch origin $baseBranch
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git reset --hard "origin/$baseBranch"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git clean -ffdx
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $status = git status --porcelain=v1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($status) {
      Write-Error "Workspace is dirty before dispatch:`n$status"
      exit 1
    }

    $config = "C:\Users\iomancer\.codex\config.toml"
    $configDir = Split-Path -Parent $config
    if (-not (Test-Path -LiteralPath $configDir)) {
      New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    }
    if (-not (Test-Path -LiteralPath $config)) {
      New-Item -ItemType File -Path $config -Force | Out-Null
    }

    $workspace = (Get-Location).Path
    $trustedProjects = @("D:\Source", "D:\Source\symphony-workspaces", $workspace)
    foreach ($project in $trustedProjects) {
      $header = "[projects.'$($project.ToLowerInvariant())']"
      $trustBlock = "`n$header`ntrust_level = `"trusted`"`n"
      if (-not (Select-String -LiteralPath $config -SimpleMatch $header -Quiet)) {
        Add-Content -LiteralPath $config -Value $trustBlock
      }
    }
    exit 0
agent:
  max_concurrent_agents: 1
  max_turns: 12
codex:
  command: codex --config shell_environment_policy.inherit=all --config "shell_environment_policy.set.PATH='C:\Users\iomancer\AppData\Local\Microsoft\WinGet\Links;C:\Program Files\Git\cmd;C:\Program Files\dotnet;C:\Program Files\PowerShell\7;C:\Program Files\nodejs;C:\Program Files\GitHub CLI;C:\Windows\System32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0;C:\Windows\System32\OpenSSH;C:\Users\iomancer\AppData\Roaming\npm;C:\Users\iomancer\.dotnet\tools;C:\Users\iomancer\.cargo\bin;C:\Users\iomancer\AppData\Local\Microsoft\WindowsApps'" --config model_reasoning_effort=medium --model gpt-5.5 app-server
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
6. Validate with `.\scripts\validate-symphony.ps1` at minimum. If you are blocked, report the exact failing phase and command output.
7. If you discover out-of-scope useful work, create or propose a separate Linear follow-up issue instead of expanding scope.
8. Final response must summarize changes, validation, blockers, and any follow-up issues.
9. Do not move implementation issues directly to Done. End code work in Ready for Review with a review packet.
10. Before Ready for Review, attach or describe a reviewable artifact: branch, commit, PR URL, patch location, or clear reason there is no code artifact.

Review packet:

- Summary of what changed.
- Files changed.
- Validation command and result.
- Artifact link or exact workspace/branch/commit path.
- Risks, blockers, and follow-up issues.

Environment note:

- The workspace root `D:\Source\symphony-workspaces` should be trusted in Codex.
- The `after_create` hook also adds the exact generated workspace path to `C:\Users\iomancer\.codex\config.toml` before `codex app-server` starts.
- Use `.\scripts\validate-symphony.ps1` instead of ad hoc build commands. It runs the Operator web build through `npm.cmd`, clears stale ASP.NET static web asset metadata after Vite changes hashed files, and builds `dotnet\Symphony.slnx` single-node with NuGet audit disabled.
- If only .NET code changed, `.\scripts\validate-symphony.ps1 -SkipWeb` is acceptable. If frontend assets changed, do not hand-edit `wwwroot\operator\index.html`; run the script and commit the Vite-generated asset names.
