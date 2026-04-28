---
name: land
description:
  Land a PR by monitoring conflicts, resolving failures, waiting for checks,
  squash-merging, and moving the Linear issue to Done.
---

# Land

## Goals

- Target `bestlux/symphony`, not `openai/symphony`.
- Ensure the PR branch is synced with `origin/main`.
- Keep validation and PR body checks green.
- Address review comments and failed checks before merging.
- Squash-merge the PR, then move Linear to `Done` and update `## Codex Workpad`.

## Preconditions

- `gh` CLI is authenticated.
- `GH_REPO=bestlux/symphony`.
- You are on the PR branch with a clean or intentionally committable working
  tree.
- The Linear issue is in `Merging`.

## Steps

1. Set `GH_REPO=bestlux/symphony`.
2. Locate the PR for the current branch.
3. If local changes exist, run the `commit` skill and then the `push` skill.
4. Run `.\scripts\validate-symphony.ps1`.
5. Validate the live PR body with `.\scripts\check-pr-body.ps1`.
6. Check PR mergeability.
7. If conflicts exist, run the `pull` skill, then `push`.
8. Review comments:
   - Human comments are blocking until answered and addressed.
   - Codex comments are blocking when they identify correctness or validation
     issues; answer with a `[codex]` comment and fix or explicitly defer.
9. Watch checks. If checks fail, inspect logs, fix, commit, push, and restart
   the landing loop.
10. Squash-merge the PR.
11. Update the Linear workpad with merge SHA/PR URL and move the issue to
    `Done` using `linear_graphql`.

## PowerShell Commands

```powershell
$env:GH_REPO = "bestlux/symphony"
$branch = git branch --show-current
if (-not $branch) { throw "No current branch" }

$status = git status --porcelain
if ($status) {
    throw "Working tree has local changes. Run the commit and push skills before landing."
}

.\scripts\validate-symphony.ps1
if ($LASTEXITCODE -ne 0) { throw "Validation failed before landing" }

$pr = gh pr view --json number,title,body,url,mergeable,mergeStateStatus | ConvertFrom-Json
if (-not $pr.number) { throw "No PR found for current branch" }

$bodyFile = Join-Path $env:TEMP "symphony-pr-body.md"
$pr.body | Set-Content -LiteralPath $bodyFile -Encoding utf8
.\scripts\check-pr-body.ps1 -Path $bodyFile

$reviewComments = @(gh api "repos/$env:GH_REPO/pulls/$($pr.number)/comments" | ConvertFrom-Json)
$issueComments = @(gh api "repos/$env:GH_REPO/issues/$($pr.number)/comments" | ConvertFrom-Json)
$allComments = @($reviewComments) + @($issueComments)
$blockingComments = $allComments | Where-Object {
    $_.user.login -notmatch '\[bot\]$' -and $_.body -notmatch '\[codex\]'
}
if ($blockingComments.Count -gt 0) {
    throw "PR has review or issue comments that need explicit [codex] acknowledgement before landing."
}

if ($pr.mergeable -eq "CONFLICTING" -or $pr.mergeStateStatus -eq "DIRTY") {
    throw "PR has conflicts. Run the pull skill, push, and retry landing."
}

gh pr checks --watch
if ($LASTEXITCODE -ne 0) {
    gh pr checks
    throw "PR checks failed. Inspect gh run logs, fix, commit, push, and retry."
}

gh pr merge --squash --delete-branch --subject $pr.title --body $pr.body
if ($LASTEXITCODE -ne 0) { throw "PR merge failed" }
```

## Notes

- Do not enable auto-merge.
- Do not merge if review comments are unresolved.
- Do not move Linear to `Done` until GitHub reports the PR merged.
- If `gh` targets the wrong repo, stop and set `GH_REPO=bestlux/symphony`
  rather than changing remotes.
