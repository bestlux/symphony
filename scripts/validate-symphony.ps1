param(
    [switch] $SkipWeb,
    [switch] $SkipDotnet,
    [switch] $KeepTemp,
    [string] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dotnetRoot = Join-Path $repoRoot "dotnet"
$operatorWebRoot = Join-Path $dotnetRoot "src\Symphony.Operator.Web"
$serviceRoot = Join-Path $dotnetRoot "src\Symphony.Service"
$solution = Join-Path $dotnetRoot "Symphony.slnx"
$tempRoot = Join-Path $repoRoot ".tmp-validate"
$tempProfile = Join-Path $tempRoot "profile"
$tempAppData = Join-Path $tempRoot "appdata"
$tempLocalAppData = Join-Path $tempRoot "localappdata"
$tempDotnetHome = Join-Path $tempRoot "dotnet-home"
$tempNpmCache = Join-Path $tempRoot "npm-cache"
$tempBuild = Join-Path $tempRoot "build"

function Set-ValidationEnvironment {
    New-Item -ItemType Directory -Force -Path `
        $tempProfile, `
        $tempAppData, `
        $tempLocalAppData, `
        $tempDotnetHome, `
        $tempNpmCache | Out-Null

    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_NOLOGO = "1"
    $env:DOTNET_CLI_HOME = $tempDotnetHome
    $env:NPM_CONFIG_CACHE = $tempNpmCache
    $env:npm_config_cache = $tempNpmCache
    $env:USERPROFILE = $tempProfile
    $env:APPDATA = $tempAppData
    $env:LOCALAPPDATA = $tempLocalAppData

    if (-not $env:NUGET_PACKAGES) {
        $userNuGet = Join-Path $HOME ".nuget\packages"
        if (Test-Path -LiteralPath $userNuGet) {
            $env:NUGET_PACKAGES = $userNuGet
        }
    }
}

function Remove-InRepoDirectory {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (-not $resolved.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside repo: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Test-ServiceRunningFromRepo {
    $serviceProcesses = Get-Process Symphony.Service -ErrorAction SilentlyContinue
    foreach ($process in $serviceProcesses) {
        try {
            if ($process.Path -and $process.Path.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        catch {
            continue
        }
    }

    return $false
}

function Normalize-TextFile {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $content = $content -replace "`r`n", "`n"
    $content = $content -replace "`r", ""
    [System.IO.File]::WriteAllText($Path, $content, [System.Text.UTF8Encoding]::new($false))
}

Set-ValidationEnvironment

if (-not $SkipWeb) {
    if (-not (Test-Path -LiteralPath (Join-Path $operatorWebRoot "package.json"))) {
        throw "Operator web package.json not found at $operatorWebRoot"
    }

    Push-Location $operatorWebRoot
    try {
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm.cmd run build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    # Vite changes hashed files under wwwroot. Clear stale ASP.NET static web asset
    # metadata so the service build does not reference removed bundle names.
    Normalize-TextFile (Join-Path $serviceRoot "wwwroot\operator\index.html")
    Remove-InRepoDirectory (Join-Path $serviceRoot "obj")
}

if (-not $SkipDotnet) {
    $dotnetArgs = @(
        "test",
        $solution,
        "-m:1",
        "-p:NuGetAudit=false",
        "-p:BuildInParallel=false",
        "-p:Configuration=$Configuration",
        "-v:minimal",
        "-tl:off"
    )

    if (Test-ServiceRunningFromRepo) {
        Remove-InRepoDirectory $tempBuild
        New-Item -ItemType Directory -Force -Path $tempBuild | Out-Null
        $dotnetArgs += "-p:OutDir=$tempBuild\"
    }

    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed with exit code $LASTEXITCODE"
    }
}

if (-not $KeepTemp) {
    Remove-InRepoDirectory $tempRoot
}
