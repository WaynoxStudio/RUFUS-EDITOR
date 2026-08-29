# LIC.7 / ADMIN.1 — publish RUFUS ADMIN to dist-admin (never included in USER dist).
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$distRoot = Join-Path $repoRoot "dist-admin\RUFUS ADMIN"
if (Test-Path $distRoot) { Remove-Item -Recurse -Force $distRoot }
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

Write-Host "=== Publish RufusAdmin.exe ==="
dotnet publish "src\RufusMapEditor.Admin\RufusMapEditor.Admin.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $distRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$readme = @"
RUFUS ADMIN - uso interno
=========================

NO distribuir. NO incluir en paquete USER.

Ejecutar: RufusAdmin.exe

Conexion:
  Tras conectar con exito, Base URL + Admin Secret se guardan en el perfil
  Windows del usuario (%LocalAppData%\RufusMapEditor\admin-connection.bin)
  protegidos con DPAPI. Al reabrir ADMIN se restaura y conecta solo.

  Copiar esta carpeta dist-admin a otro PC NO transporta el secret.

Mapas (ADMIN.UI.2):
  Reutiliza el mismo modulo MapsEditorView que RUFUS Map Editor.
  Library / DB / SFTP: misma config (%LocalAppData%\RufusMapEditor\settings.json)
  o Library junto al EXE. NO duplica Library dentro de dist-admin.

Contenido / NPC (ADMIN.UI.3 / UI.3.2):
  Reutiliza ContentWorkspaceView (mismo codigo que Hub → Contenido en USER).
  Generar nombre en Identidad (exactamente 3 propuestas; Usar aplica al draft).
  Generar dialogo / conversacion en Interacciones.

IA ADMIN (ADMIN.AI.1):
  POST /v1/admin/ai-session (auth Admin) emite token temporal rai1.*
  /v1/ai/generate acepta ese token; rechaza RUFUS_ADMIN_API_SECRET directo.
  Sesion IA solo en memoria del proceso (no DPAPI del token IA).
  Requiere backend actualizado en VPS (ver ADMIN.AI.1P / ADMIN.P.1).
  USER sigue con SessionToken / permission.ai / quotas.

Uso IA (ADMIN.USAGE.1):
  GET /v1/admin/ai-usage — metricas agregadas (tokens/generaciones).
  Solo lectura de rufus_ai_usage_events (eventos USER con licencia).
  Telemetria IA ADMIN (rai1) pendiente. Sin claves OpenAI en ADMIN.

Opcional (desarrollo / primera vez):
  RUFUS_ADMIN_API_BASE
  RUFUS_ADMIN_API_SECRET
  RUFUS_ADMIN_AI_SESSION_MINUTES  (backend; default 60)

Nunca incluir secretos en esta carpeta ni en Git.

"@
[System.IO.File]::WriteAllText((Join-Path $distRoot "README.txt"), $readme, [System.Text.UTF8Encoding]::new($false))

Write-Host "ADMIN dist: $distRoot"
Write-Host "ADMIN.1 build OK"
