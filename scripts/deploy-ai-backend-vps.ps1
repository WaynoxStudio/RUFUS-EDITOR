# LIC.4 — publish + deploy RufusMapEditor.AiBackend (linux-x64) to VPS.
# Does NOT print secrets. Requires RUFUS_DEPLOY_PWD / RUFUS_DEPLOY_USER and RUFUS_ADMIN_API_SECRET_PROD in env.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($env:RUFUS_DEPLOY_PWD)) { throw "RUFUS_DEPLOY_PWD required" }
if ([string]::IsNullOrWhiteSpace($env:RUFUS_ADMIN_API_SECRET_PROD)) { throw "RUFUS_ADMIN_API_SECRET_PROD required" }

$out = Join-Path $repoRoot "publish\RufusAiBackend"
Write-Host "=== Publish ==="
dotnet publish "src\RufusMapEditor.AiBackend\RufusMapEditor.AiBackend.csproj" -c Release -r linux-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path (Join-Path $out "RufusMapEditor.Licensing.dll"))) { throw "Licensing.dll missing from publish" }

$env:RUFUS_DEPLOY_LOCAL = $out
$env:RUFUS_LICENSE_DB_PATH_PROD = "/home/ubuntu/RufusAiBackend/data/rufus-licenses.db"

python "$PSScriptRoot\deploy_ai_backend_vps.py"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "DEPLOY OK"
