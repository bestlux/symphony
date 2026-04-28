---
tracker:
  kind: linear
  api_key: $LINEAR_API_KEY
  project_slug: "symphony-net-e7a506c9659d"
  active_states:
    - Todo
    - In Progress
    - Merging
    - Rework
  dispatch_states:
    - Todo
    - In Progress
    - Merging
    - Rework
  terminal_states:
    - Closed
    - Cancelled
    - Canceled
    - Duplicate
    - Done
polling:
  interval_ms: 5000
workspace:
  root: D:/Source/symphony-workspaces
hooks:
  timeout_ms: 300000
  after_create: |
    $ErrorActionPreference = "Stop"
    $source = "git@github.com:bestlux/symphony.git"
    $baseBranch = if ([string]::IsNullOrWhiteSpace($env:SYMPHONY_BASE_BRANCH)) { "main" } else { $env:SYMPHONY_BASE_BRANCH }
    $issueId = if ([string]::IsNullOrWhiteSpace($env:SYMPHONY_ISSUE_IDENTIFIER)) { "issue" } else { $env:SYMPHONY_ISSUE_IDENTIFIER.ToLowerInvariant() }
    $branch = $env:SYMPHONY_ISSUE_BRANCH
    if ([string]::IsNullOrWhiteSpace($branch)) {
      $branch = "codex/$issueId"
    }

    git clone --no-local --branch $baseBranch --single-branch $source .
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git remote remove upstream 2>$null
    git remote add upstream https://github.com/openai/symphony.git
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git fetch origin $baseBranch
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git reset --hard "origin/$baseBranch"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git clean -ffdx
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git checkout -B $branch
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
  before_remove: |
    $ErrorActionPreference = "Continue"
    $env:GH_REPO = "bestlux/symphony"
    $branch = git branch --show-current
    if (-not [string]::IsNullOrWhiteSpace($branch)) {
      $prs = gh pr list --repo bestlux/symphony --head $branch --state open --json number --jq ".[].number" 2>$null
      foreach ($pr in $prs) {
        gh pr comment $pr --repo bestlux/symphony --body "Closing because the Linear issue for branch $branch entered a terminal state without merge."
        gh pr close $pr --repo bestlux/symphony
      }
    }
agent:
  max_concurrent_agents: 10
  max_turns: 20
codex:
  command: codex --config shell_environment_policy.inherit=all --config "shell_environment_policy.set.PATH='C:\Users\iomancer\AppData\Local\Microsoft\WinGet\Links;C:\Program Files\Git\cmd;C:\Program Files\dotnet;C:\Program Files\PowerShell\7;C:\Program Files\nodejs;C:\Program Files\GitHub CLI;C:\Windows\System32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0;C:\Windows\System32\OpenSSH;C:\Users\iomancer\AppData\Roaming\npm;C:\Users\iomancer\.dotnet\tools;C:\Users\iomancer\.cargo\bin;C:\Users\iomancer\AppData\Local\Microsoft\WindowsApps'" --config "shell_environment_policy.set.GH_REPO='bestlux/symphony'" --config model_reasoning_effort=xhigh --model gpt-5.5 app-server
  approval_policy: never
  thread_sandbox: workspace-write
  turn_sandbox_policy:
    type: workspaceWrite
---

You are working on a Linear ticket `{{ issue.identifier }}` for the .NET Symphony implementation.

{% if attempt %}
Continuation context:

- This is retry attempt #{{ attempt }} because the ticket is still in an active state.
- Resume from the current workspace state instead of restarting from scratch.
- Do not repeat already-completed investigation or validation unless needed for new code changes.
- Do not end the turn while the issue remains in an active state unless you are blocked by missing required permissions, secrets, or tools.
{% endif %}

Repo target: `bestlux/symphony`
Workspace: this isolated per-issue checkout, never `D:\Source\symphony`.

Issue context:
Identifier: {{ issue.identifier }}
Title: {{ issue.title }}
Current status: {{ issue.state }}
Labels: {{ issue.labels }}
Branch: {{ issue.branch_name }}
URL: {{ issue.url }}

Description:
{% if issue.description %}
{{ issue.description }}
{% else %}
No description provided.
{% endif %}

## Required posture

1. This is an unattended orchestration session. Never ask a human to perform follow-up actions.
2. Only stop early for a true blocker: missing required auth, permissions, secrets, or tools after documented fallbacks fail.
3. Work only in this repository copy.
4. Final message reports completed actions and blockers only. Do not include "next steps for user".
5. Keep Linear, the PR, and the single workpad comment current as the durable record.

## Required Linear/GitHub tools

- Use `linear_graphql` for Linear reads/writes when available.
- Use the repo-local skills: `linear`, `commit`, `pull`, `push`, and `land`.
- `GH_REPO` is set to `bestlux/symphony`; PRs must target that repo, not `openai/symphony`.

## Status map

- `Backlog`: out of scope; do not modify.
- `Todo`: queued; immediately move to `In Progress`, then create/update the workpad and execute.
- `In Progress`: implementation is active; continue from the workpad.
- `Human Review`: PR is attached and validated; wait only.
- `Merging`: human approved; run the `land` skill loop until merged, then move to `Done`.
- `Rework`: reviewer requested changes; restart from a fresh approach while preserving issue context.
- `Done`: terminal; do nothing.

## Step 0: Route by current state

1. Fetch this issue by ID and read its current state.
2. Route exactly:
   - `Backlog`: stop without modifying issue content/state.
   - `Todo`: move to `In Progress`, then start Step 1.
   - `In Progress`: start Step 1.
   - `Human Review`: wait and poll for a decision; do not code.
   - `Merging`: open `.codex/skills/land/SKILL.md` and follow it until the PR is merged.
   - `Rework`: follow Step 4.
   - `Done`: stop.
3. If a PR already exists for this branch and is closed or merged, create a fresh branch from `origin/main` and restart execution.

## Step 1: Workpad

Find or create exactly one active Linear comment with this marker:

```md
## Codex Workpad
```

Use that one comment for all progress updates. Do not create separate completion summary comments.

The workpad must use this structure:

````md
## Codex Workpad

```text
<hostname>:<abs-workdir>@<short-sha>
```

### Plan

- [ ] 1. Parent task
  - [ ] 1.1 Child task

### Acceptance Criteria

- [ ] Criterion

### Validation

- [ ] targeted tests: `<command>`

### Notes

- <short progress note with timestamp>

### Confusions

- <only include when something was confusing>
````

Before editing code:

1. Reconcile the existing workpad.
2. Add or update plan, acceptance criteria, and validation.
3. Record a concrete reproduction or current-state signal.
4. Run the `pull` skill to sync with latest `origin/main`.
5. Record pull evidence: source, result, and resulting short SHA.

## Step 2: Implementation and PR handoff

1. Implement only the issue scope.
2. Update the workpad after meaningful milestones.
3. Run the required validation for the issue. For this repo:
   - Use `.\scripts\validate-symphony.ps1` for full validation.
   - Use `.\scripts\validate-symphony.ps1 -SkipWeb` only when no web/operator assets changed.
   - If the issue specifies a validation/test plan, it is mandatory.
4. Commit with the `commit` skill.
5. Push and create/update a PR with the `push` skill.
6. Attach or link the PR to Linear.
7. Poll PR feedback and checks. Resolve or explicitly push back on every actionable comment.
8. Move the issue to `Human Review` only after:
   - workpad plan/acceptance/validation are complete,
   - branch is pushed,
   - PR is linked,
   - validation is green,
   - no actionable PR feedback remains.

## Step 3: Human Review and landing

1. In `Human Review`, do not code.
2. If review feedback requires changes, move the issue to `Rework`.
3. If approved, the human moves the issue to `Merging`.
4. In `Merging`, run `.codex/skills/land/SKILL.md`.
5. After the PR is merged, move the issue to `Done`.

## Step 4: Rework

1. Re-read issue body, workpad, PR comments, and human comments.
2. Identify what will be done differently.
3. Close any stale PR that should not be reused.
4. Create or switch to a fresh branch from `origin/main` when the current PR is closed/merged or the branch is not reusable.
5. Reset the workpad checklist for the new attempt.
6. Execute Step 2 again.

## Follow-up issue policy

If useful out-of-scope work is discovered, create a separate Linear issue in `Backlog` with:

- clear title,
- description,
- acceptance criteria,
- same project,
- `related` link to the current issue,
- `blockedBy` when the follow-up depends on the current issue.

Do not expand the current issue to absorb out-of-scope work.

## Blocked-access escape hatch

Use this only for missing required auth, permissions, secrets, or tools after fallbacks fail.

If blocked:

1. Update the workpad with what is missing, why it blocks completion, and the exact unblock action.
2. Move the issue to `Human Review`.
3. Final response reports the blocker only.
