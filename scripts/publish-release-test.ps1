# Publishes Release test builds:
#   - artifacts/RufusMapEditor/  (framework-dependent, multi-file, for debugging)
#   - RufusMapEditor.exe           (self-contained single-file launcher at repo root)
# Param: -RufusEdition User | Development (default Development)
param(
    [ValidateSet("User", "Development")]
    [string]$RufusEdition = "Development"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "RufusEdition=$RufusEdition"

dotnet restore src/RufusMapEditor.App/RufusMapEditor.App.csproj -r win-x64 | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet msbuild src/RufusMapEditor.App/RufusMapEditor.App.csproj -t:PublishReleaseTest -p:RufusEdition=$RufusEdition -v:m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Artifacts (debug):" (Join-Path $repoRoot "artifacts\RufusMapEditor\RufusMapEditor.exe")
Write-Host "Root launcher:" (Join-Path $repoRoot "RufusMapEditor.exe")
