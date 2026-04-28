---
name: push
description:
  Push current branch changes to origin and create or update the corresponding
  pull request; use when asked to push, publish updates, or create pull request.
---

# Push

## Prerequisites

- `gh` CLI is installed and authenticated.
- `GH_REPO` is set to `bestlux/symphony`, or every `gh` command uses
  `--repo bestlux/symphony`.
- The current branch is the Linear branch for the issue.

## Goals

- Run the .NET/web validation gate before publishing.
- Push the current branch to `origin`.
- Create or update a GitHub PR against `bestlux/symphony:main`.
- Link the PR in Linear and update the persistent `## Codex Workpad` comment.

## Related Skills

- `pull`: use this when the branch is stale or push is rejected as
  non-fast-forward.
- `linear`: use this to edit the Linear workpad through `linear_graphql`.

## Steps

1. Confirm branch and remotes.
2. Run `.\scripts\validate-symphony.ps1`.
3. Push with upstream tracking: `git push -u origin HEAD`.
4. If push is rejected because the branch is stale, run the `pull` skill, rerun
   validation, and retry the push.
5. Ensure a PR exists for the branch:
   - Create one if missing.
   - Update title/body if it already exists and the scope changed.
   - If the branch is tied to a closed or merged PR, create a fresh branch from
     `origin/main` and restart.
6. Draft a concrete PR body from `.github/pull_request_template.md` before
   creating or editing the PR. Remove every placeholder comment.
7. Validate the drafted PR body with `.\scripts\check-pr-body.ps1`.
8. Create or update the PR with the validated body.
9. Update Linear:
   - Add the PR URL to `## Codex Workpad`.
   - Record validation results and the current commit.
   - Move the issue to `Human Review` only after the PR and workpad are ready.
10. Reply with the PR URL and the validation command result.

## PowerShell Commands

```powershell
$env:GH_REPO = "bestlux/symphony"
$branch = git branch --show-current
if (-not $branch) { throw "No current branch" }

.\scripts\validate-symphony.ps1
if ($LASTEXITCODE -ne 0) { throw "Validation failed" }

git push -u origin HEAD
if ($LASTEXITCODE -ne 0) {
    throw "Push failed. If this is non-fast-forward, run the pull skill, then validate and push again."
}

$pr = gh pr view --json number,state,url 2>$null | ConvertFrom-Json
if ($pr.state -eq "MERGED" -or $pr.state -eq "CLOSED") {
    throw "Current branch is tied to a closed PR; create a fresh branch from origin/main."
}

$title = "<clear PR title written for this issue>"
$bodyFile = Join-Path $env:TEMP "symphony-pr-body.md"
Copy-Item -LiteralPath .github\pull_request_template.md -Destination $bodyFile -Force

# Replace every template placeholder in $bodyFile with concrete issue, PR,
# validation, and risk details before continuing.
.\scripts\check-pr-body.ps1 -Path $bodyFile

if (-not $pr) {
    gh pr create --base main --head $branch --title $title --body-file $bodyFile
} else {
    gh pr edit --title $title --body-file $bodyFile
}

gh pr view --json url -q .url
```

## Notes

- Do not push to `openai/symphony`.
- Do not use `--force`; use `--force-with-lease` only after deliberate local
  history rewrite.
- Auth, permission, and missing-remote failures are blockers. Surface the exact
  error instead of changing remotes opportunistically.
