[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version   # e.g. "1.4.0" - no versioning convention exists in this repo yet;
                        # kept as a manual required param rather than inventing a git-tag scheme.
)

$ErrorActionPreference = 'Stop'

$deployPath = $env:MULTICLOD_DEPLOY_PATH
if ([string]::IsNullOrWhiteSpace($deployPath)) {
    Write-Error "MULTICLOD_DEPLOY_PATH is not set. This must point at the update feed's network share and is intentionally never committed to this repo - set it in your own shell/profile before running this script."
    exit 1
}

$repoRoot   = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\MultiClod.App\MultiClod.App.csproj'
$publishDir = Join-Path $repoRoot 'publish\MultiClod.App'          # already covered by .gitignore's `publish/` rule
$releaseDir = Join-Path $repoRoot 'publish\MultiClod.App.Release'

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }

Write-Host "Publishing MultiClod.App $Version (framework-dependent, win-x64)..."
dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:MultiClodUpdateFeedPath=$deployPath `
    -p:Version=$Version `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "vpk not found - installing dotnet tool (version 1.2.0)..."
    dotnet tool install -g vpk --version 1.2.0
    if ($LASTEXITCODE -ne 0) { throw "Failed to install vpk. Install manually: dotnet tool install -g vpk --version 1.2.0" }
}

Write-Host "Packing with vpk (framework-dependent - bootstraps .NET 8 Desktop Runtime if missing)..."
vpk pack `
    --packId MultiClod.App `
    --packTitle 'Multi-Clod' `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe MultiClod.App.exe `
    --icon (Join-Path $repoRoot 'src\MultiClod.App\Assets\app.ico') `
    --framework net8-x64-desktop `
    --outputDir $releaseDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

Write-Host "Publishing release feed to deploy path..."
# robocopy over Copy-Item: retries/resumes cleanly on flaky network shares; its own exit codes
# 0-7 are all "success" variants, so PowerShell's default non-zero-is-error needs overriding.
robocopy $releaseDir $deployPath /E /R:3 /W:5 /NFL /NDL
if ($LASTEXITCODE -ge 8) { throw "robocopy failed copying release feed to deploy path (exit code $LASTEXITCODE)." }

Write-Host "Published MultiClod.App $Version to feed."
# robocopy's own exit codes 0-7 are all "success" variants (eg. 1 = files copied) but would
# otherwise leak out as this script's exit code and read as a failure to any caller - reset it
# now that we've already validated success above.
exit 0
