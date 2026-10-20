# Portable package is RufusAdmin only (map editor + NPC/diálogos + ajustes).
# Delegates to build-admin-dist.ps1 — no separate RufusMapEditor.exe.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "=== RUFUS: único producto de escritorio = RufusAdmin.exe ==="
& (Join-Path $PSScriptRoot "build-admin-dist.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$adminDist = Join-Path $repoRoot "dist-admin\RUFUS ADMIN"
$userDist = Join-Path $repoRoot "dist\RUFUS ADMIN"
if (Test-Path $userDist) { Remove-Item -Recurse -Force $userDist }
New-Item -ItemType Directory -Path (Split-Path $userDist) -Force | Out-Null
Copy-Item $adminDist $userDist -Recurse -Force

Write-Host "Mirror copy: $userDist"
Write-Host "Abrir: RufusAdmin.exe"
