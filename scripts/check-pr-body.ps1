param(
    [Parameter(Mandatory)]
    [string] $Path
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Path)) {
    throw "PR body file not found: $Path"
}

$body = Get-Content -LiteralPath $Path -Raw
$requiredHeadings = @(
    "#### Context",
    "#### TL;DR",
    "#### Summary",
    "#### Review Packet",
    "#### Linear",
    "#### Validation",
    "#### Risks"
)

$missing = @()
foreach ($heading in $requiredHeadings) {
    if (-not $body.Contains($heading, [System.StringComparison]::Ordinal)) {
        $missing += $heading
    }
}

if ($missing.Count -gt 0) {
    throw "PR body is missing required heading(s): $($missing -join ', ')"
}

if ($body -match "<!--") {
    throw "PR body still contains template placeholder comments."
}

if ($body -match "(?m)^\s*-\s+\[\s\]") {
    throw "PR body has unchecked validation items. Mark each checkbox complete or replace it with an explicit not-run explanation."
}

if ($body -notmatch "(?m)^\s*-\s+\[x\]\s+`?\.\\scripts\\validate-symphony\.ps1`?") {
    throw "PR body must include completed validation for .\scripts\validate-symphony.ps1."
}

Write-Host "PR body check passed."
