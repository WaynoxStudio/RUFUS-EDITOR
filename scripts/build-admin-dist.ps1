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

# CONTENT.ITEMS.RUNTIME_TOOLS.INTEGRATE.1 — silent FFDec payload → LocalAppData tools-cache.
# CONTENT.ITEMS.RUNTIME_JRE.INTEGRATE.1 — Eclipse Temurin JRE 21 payload (GPLv2+CE).
$ffdecSrc = Join-Path $repoRoot "tools\_ffdec"
if (-not (Test-Path (Join-Path $ffdecSrc "ffdec-cli.exe"))) {
    $ffdecSrc = Join-Path $repoRoot "tools\_ffdec\extracted"
}
if (-not (Test-Path (Join-Path $ffdecSrc "ffdec-cli.exe"))) {
    throw "FFDec source no encontrado (tools\_ffdec)."
}
$payloadDir = Join-Path $distRoot "payload"
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
$payloadZip = Join-Path $payloadDir "ffdec-runtime.zip"
Write-Host "=== Pack FFDec runtime payload ==="
$packStaging = Join-Path $env:TEMP ("ffdec-admin-pack-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $packStaging -Force | Out-Null
try {
    foreach ($name in @("ffdec-cli.exe", "ffdec-cli.jar", "ffdec.jar")) {
        $src = Join-Path $ffdecSrc $name
        if (Test-Path $src) { Copy-Item $src (Join-Path $packStaging $name) -Force }
    }
    $libSrc = Join-Path $ffdecSrc "lib"
    if (-not (Test-Path $libSrc)) { throw "FFDec lib/ no encontrado en $ffdecSrc" }
    Copy-Item $libSrc (Join-Path $packStaging "lib") -Recurse -Force
    [IO.File]::WriteAllText((Join-Path $packStaging "runtime.version"), "ffdec-26.2.1-min1`n", [Text.UTF8Encoding]::new($false))
    if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
    Compress-Archive -Path (Join-Path $packStaging "*") -DestinationPath $payloadZip -CompressionLevel Optimal
    Write-Host "Payload: $payloadZip ($([math]::Round((Get-Item $payloadZip).Length/1MB,2)) MB)"
}
finally {
    Remove-Item $packStaging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "=== Pack Temurin JRE 21 payload ==="
$jreDir = Join-Path $repoRoot "tools\_jre"
$jreZipName = "OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip"
$jreZipSrc = Join-Path $jreDir $jreZipName
$jreUrl = "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip"
$jreSha = "d35f31e712f0fcf6ac5a093edc90204fbff22f720ba3950bd09d331d5e621636"
New-Item -ItemType Directory -Path $jreDir -Force | Out-Null
if (-not (Test-Path $jreZipSrc) -or ((Get-FileHash $jreZipSrc -Algorithm SHA256).Hash.ToLower() -ne $jreSha)) {
    Write-Host "Downloading Eclipse Temurin JRE 21 (official Adoptium)..."
    Invoke-WebRequest -Uri $jreUrl -OutFile $jreZipSrc -UseBasicParsing
}
$actualSha = (Get-FileHash $jreZipSrc -Algorithm SHA256).Hash.ToLower()
if ($actualSha -ne $jreSha) { throw "JRE SHA256 mismatch: $actualSha" }
$jrePayload = Join-Path $payloadDir "jre-runtime.zip"
Copy-Item $jreZipSrc $jrePayload -Force
Write-Host "Payload: $jrePayload ($([math]::Round((Get-Item $jrePayload).Length/1MB,2)) MB)"

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

Herramientas runtime (items preview):
  payload\ffdec-runtime.zip + payload\jre-runtime.zip se extraen bajo demanda a
  %LocalAppData%\RufusMapEditor\tools-cache\ffdec\ y ...\jre\
  JRE: Eclipse Temurin 21 (OpenJDK, GPLv2+Classpath Exception).
  FFDec se invoca con java.exe gestionado (sin PATH/registro).
  No abrir ni gestionar ffdec.exe / Java manualmente.

Opcional (desarrollo / primera vez):
  RUFUS_ADMIN_API_BASE
  RUFUS_ADMIN_API_SECRET
  RUFUS_ADMIN_AI_SESSION_MINUTES  (backend; default 60)

Nunca incluir secretos en esta carpeta ni en Git.

"@
[System.IO.File]::WriteAllText((Join-Path $distRoot "README.txt"), $readme, [System.Text.UTF8Encoding]::new($false))

Write-Host "ADMIN dist: $distRoot"
Write-Host "ADMIN.1 build OK"
