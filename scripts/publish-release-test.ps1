# Publishes the only desktop product: RufusAdmin.exe
#   - artifacts/RufusAdmin/       (framework-dependent, for debugging)
#   - RufusAdmin.exe at repo root (optional single-file copy via build-admin-dist)
param(
    [ValidateSet("User", "Development")]
    [string]$RufusEdition = "Development"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "RufusEdition=$RufusEdition (hosted UI only — RufusAdmin)"
Write-Host "Use scripts\build-admin-dist.ps1 for the distributable RufusAdmin.exe package."

$out = Join-Path $repoRoot "artifacts\RufusAdmin"
New-Item -ItemType Directory -Path $out -Force | Out-Null

dotnet publish "src\RufusMapEditor.Admin\RufusMapEditor.Admin.csproj" `
    -c Release -r win-x64 --self-contained false `
    -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Artifacts:" (Join-Path $out "RufusAdmin.exe")
