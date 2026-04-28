---
name: pull
description:
  Pull latest origin/main into the current local branch and resolve merge
  conflicts. Use when Codex needs to sync a feature branch with origin/main.
---

# Pull

## Workflow

1. Verify the current branch and working tree.
2. Enable rerere locally:
   - `git config rerere.enabled true`
   - `git config rerere.autoupdate true`
3. Fetch `origin`.
4. Fast-forward the current feature branch from its remote tracking branch when
   one exists.
5. Merge `origin/main` with `zdiff3` conflict style.
6. Resolve conflicts by understanding both sides first, then editing the
   smallest correct result.
7. Run `git diff --check`.
8. Run `.\scripts\validate-symphony.ps1`.
9. Summarize conflicts, validation, and any assumptions.

## PowerShell Commands

```powershell
$branch = git branch --show-current
if (-not $branch) { throw "No current branch" }

git status --short
git config rerere.enabled true
git config rerere.autoupdate true
git fetch origin

$upstream = git rev-parse --abbrev-ref --symbolic-full-name "@{u}" 2>$null
if ($LASTEXITCODE -eq 0 -and $upstream) {
    git pull --ff-only origin $branch
    if ($LASTEXITCODE -ne 0) { throw "Could not fast-forward current branch from origin/$branch" }
}

git -c merge.conflictstyle=zdiff3 merge origin/main
if ($LASTEXITCODE -ne 0) {
    git status --short
    throw "Merge conflicts need resolution. Resolve them, stage files, and continue the merge."
}

git diff --check
if ($LASTEXITCODE -ne 0) { throw "Whitespace/conflict-marker check failed" }

.\scripts\validate-symphony.ps1
if ($LASTEXITCODE -ne 0) { throw "Validation failed after pull" }
```

## Conflict Guidance

- Inspect `git status`, `git diff`, and `git diff --merge` before editing.
- Prefer preserving both sides when they are compatible.
- Avoid `ours` or `theirs` unless one side clearly supersedes the other.
- For generated files, resolve source first and regenerate.
- Ask only when product intent cannot be inferred from code or the issue.
