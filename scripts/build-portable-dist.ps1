# DIST.1 — Builds clean portable user package:
#   dist\RUFUS Map Editor\  (+ ZIP)
# Does NOT ship: src, tests, tools, AiBackend, RufusAdmin/Admin, secrets, LocalAppData, developer queue.
# Source: repo Master Library (RUFUS EDITOR\Library) — NOT the legacy Astria install.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$distRoot = Join-Path $repoRoot "dist\RUFUS Map Editor"
$libraryDest = Join-Path $distRoot "Library"
$zipPath = Join-Path $repoRoot "dist\RUFUS Map Editor.zip"
$masterLibrary = Join-Path $repoRoot "Library"

if (-not (Test-Path $masterLibrary)) {
    throw "No se encontró la Master Library de RUFUS: $masterLibrary"
}
if (-not (Test-Path (Join-Path $masterLibrary "Maps"))) {
    throw "Master Library inválida (falta Maps): $masterLibrary"
}

Write-Host "=== Publish RufusMapEditor.exe (USER edition - licensing enforced) ==="
Stop-Process -Name RufusMapEditor -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
& (Join-Path $PSScriptRoot "publish-release-test.ps1") -RufusEdition User
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$stagingExe = Join-Path $repoRoot "artifacts\_root_launcher_publish\RufusMapEditor.exe"
$rootExe = Join-Path $repoRoot "RufusMapEditor.exe"
$publishExe = if (Test-Path $rootExe) { $rootExe } else { $stagingExe }
if (-not (Test-Path $publishExe)) { throw "Published EXE not found" }

Write-Host "=== Prepare clean dist folder ==="
if (Test-Path $distRoot) { Remove-Item -Recurse -Force $distRoot }
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

Copy-Item $publishExe (Join-Path $distRoot "RufusMapEditor.exe") -Force

Write-Host "=== Copy Master Library (portable runtime assets only) ==="
# Exclude developer/temp/private noise; keep GFX/maps/XML/Flasm/Visuals.
$xd = @("cache", "Cache", "temp", "Temp", "tmp", "Tmp", "logs", "Logs", "obj", "bin", ".git")
$xf = @("*.env", "*.env.*", "*.pem", "*.key", "openai*.key", "*.log", "*.tmp", "*.bak", "*.old", "Thumbs.db", "desktop.ini")
$xdArgs = foreach ($d in $xd) { @("/XD", $d) }
$xfArgs = foreach ($f in $xf) { @("/XF", $f) }
New-Item -ItemType Directory -Path $libraryDest -Force | Out-Null
& robocopy $masterLibrary $libraryDest /E /NFL /NDL /NJH /NJS /NC /NS /NP @xdArgs @xfArgs | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE): $masterLibrary" }
$global:LASTEXITCODE = 0

# Do not ship developer publish-queue state (empty portable queue).
$queueDir = Join-Path $libraryDest "PublishQueue"
New-Item -ItemType Directory -Path $queueDir -Force | Out-Null
Set-Content -Path (Join-Path $queueDir "queue.json") -Value "{`r`n  `"version`": 2,`r`n  `"items`": []`r`n}`r`n" -Encoding utf8

$mapCount = (Get-ChildItem (Join-Path $libraryDest "Maps") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+$' -and (Test-Path (Join-Path $_.FullName "$($_.Name).sql")) }).Count
Write-Host "Maps in library: $mapCount"

Write-Host "=== README.txt ==="
$readme = @"
RUFUS Map Editor — distribución portable
========================================

Abrir: RufusMapEditor.exe (doble clic)

Requisitos:
  - Windows 64 bits (x64)
  - Licencia RUFUS activa (activación online obligatoria)
  - No requiere instalar .NET
  - No requiere Astria Map Editor instalado

Activación:
  Al abrir RufusMapEditor.exe por primera vez se solicitará su código de licencia.
  La sesión se guarda de forma segura en este equipo (%LocalAppData%\RufusMapEditor\).

Contenido del paquete:
  RufusMapEditor.exe   — editor (self-contained)
  Library\             — GFX, XML, Flasm, mapas iniciales, Visuals
  README.txt
  manifest.json

Biblioteca:
  La carpeta Library incluida es la biblioteca portable RUFUS.
  Contiene imágenes GFX, XML de anclas, Flasm (export SWF AME) y mapas iniciales.

Configuración del usuario (NO va en este paquete):
  %LocalAppData%\RufusMapEditor\
    settings.json   — favoritos, recientes, layout, tema, BD/SFTP (protegidos)
    autosave\       — recuperación automática

Proyectos:
  Los archivos .rufmap se guardan donde usted elija (Archivo → Guardar / Guardar como).

Export SWF AME:
  Archivo → Exportar → SWF (requiere Flasm y blank.swf en Library\Flasm).

Asistente IA:
  Disponible en Contenido si su licencia incluye IA. Requiere conexión al servicio RUFUS.

Licencia:
  No comparta su código de licencia. Cerrar sesión: Ajustes → Licencia → Cerrar sesión.

"@
Set-Content -Path (Join-Path $distRoot "README.txt") -Value $readme -Encoding UTF8

Write-Host "=== manifest.json ==="
function Get-Sha256($path) {
    if (-not (Test-Path $path)) { return $null }
    return (Get-FileHash $path -Algorithm SHA256).Hash
}
$exePath = Join-Path $distRoot "RufusMapEditor.exe"
$flasmDest = Join-Path $libraryDest "Flasm"
$manifest = [ordered]@{
    product = "RUFUS Map Editor"
    edition = "User"
    licenseEnforced = $true
    buildDateUtc = (Get-Date).ToUniversalTime().ToString("o")
    architecture = "win-x64"
    selfContained = $true
    singleFile = $true
    includesAiBackend = $false
    includesSource = $false
    mapCount = $mapCount
    components = [ordered]@{
        exe = @{ path = "RufusMapEditor.exe"; sha256 = (Get-Sha256 $exePath); bytes = (Get-Item $exePath).Length }
        flasm = @{ path = "Library/Flasm/flasm.exe"; sha256 = (Get-Sha256 (Join-Path $flasmDest "flasm.exe")) }
        blankSwf = @{ path = "Library/Flasm/blank.swf"; sha256 = (Get-Sha256 (Join-Path $flasmDest "blank.swf")) }
        groundsXml = @{ path = "Library/XML/grounds.xml"; sha256 = (Get-Sha256 (Join-Path $libraryDest "XML\grounds.xml")) }
        objectsXml = @{ path = "Library/XML/objects.xml"; sha256 = (Get-Sha256 (Join-Path $libraryDest "XML\objects.xml")) }
    }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $distRoot "manifest.json") -Encoding UTF8

Write-Host "=== DIST.1 hygiene checks ==="
$forbiddenNamePatterns = @(
    '\.cs$', '\.csproj$', '\.slnx?$', '\.fs$', '\.vb$',
    '\.env$', '\.env\.', '\.pem$', 'openai.*\.key$',
    'AiBackend', 'AiOpenAiProbe', 'RufusAdmin', 'RufusMapEditor.Admin', 'LicenseProbe', 'appsettings\..*\.json$'
)
$forbiddenPathFragments = @(
    '\src\', '/src/', '\tests\', '/tests/', '\tools\', '/tools/',
    '\.git\', '/.git/', '\obj\', '\bin\Debug', '\bin\Release',
    'rufus-licenses.db'
)
$badFiles = @()
Get-ChildItem -LiteralPath $distRoot -Recurse -Force -File | ForEach-Object {
    $rel = $_.FullName.Substring($distRoot.Length)
    foreach ($p in $forbiddenNamePatterns) {
        if ($_.Name -match $p -or $rel -match $p) { $badFiles += $_.FullName; return }
    }
    foreach ($f in $forbiddenPathFragments) {
        if ($rel -like "*$f*") { $badFiles += $_.FullName; return }
    }
}
if ($badFiles.Count -gt 0) {
    throw ("DIST hygiene failed - forbidden files:`n" + ($badFiles -join "`n"))
}

# Secret scan: env var names OK as identifiers; real values must not appear.
$secretValues = @()
foreach ($varName in @("OPENAI_API_KEY", "RUFUS_AI_ACCESS_TOKEN")) {
    $v = [Environment]::GetEnvironmentVariable($varName)
    if (-not [string]::IsNullOrWhiteSpace($v) -and $v.Trim().Length -ge 8) {
        $secretValues += [pscustomobject]@{ Name = $varName; Value = $v.Trim() }
    }
}
$textExt = @(".txt", ".json", ".xml", ".sql", ".ini", ".md", ".config", ".csv", ".ps1", ".bat", ".cmd")
Get-ChildItem -LiteralPath $distRoot -Recurse -Force -File | Where-Object {
    $textExt -contains $_.Extension.ToLowerInvariant()
} | ForEach-Object {
    $content = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($content)) { return }
    if ($content -match '(?im)^\s*OPENAI_API_KEY\s*=\s*\S+') {
        throw "DIST secret leak: OPENAI_API_KEY assignment in $($_.FullName)"
    }
    if ($content -match '(?im)^\s*RUFUS_AI_ACCESS_TOKEN\s*=\s*\S+') {
        throw "DIST secret leak: RUFUS_AI_ACCESS_TOKEN assignment in $($_.FullName)"
    }
    foreach ($s in $secretValues) {
        if ($content.Contains($s.Value)) {
            throw "DIST secret leak: environment value of $($s.Name) found in $($_.FullName)"
        }
    }
}

# Ensure EXE does not embed current process env secret values (if set on builder machine).
if ($secretValues.Count -gt 0) {
    $exeBytes = [System.IO.File]::ReadAllBytes($exePath)
    $exeUtf8 = [System.Text.Encoding]::UTF8.GetString($exeBytes)
    $exeUtf16 = [System.Text.Encoding]::Unicode.GetString($exeBytes)
    foreach ($s in $secretValues) {
        if ($exeUtf8.Contains($s.Value) -or $exeUtf16.Contains($s.Value)) {
            throw "DIST secret leak: environment value of $($s.Name) embedded in RufusMapEditor.exe"
        }
    }
}

# Absolute developer path must not appear in text package files.
$devMarker = $repoRoot
Get-ChildItem -LiteralPath $distRoot -Recurse -Force -File | Where-Object {
    $textExt -contains $_.Extension.ToLowerInvariant()
} | ForEach-Object {
    $content = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($content -and $content.Contains($devMarker)) {
        throw "DIST path leak: developer repo path found in $($_.FullName)"
    }
}

Write-Host "=== ZIP ==="
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $distRoot -DestinationPath $zipPath -CompressionLevel Optimal

$distSize = (Get-ChildItem $distRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
$fileCount = (Get-ChildItem $distRoot -Recurse -File).Count
$zipSize = (Get-Item $zipPath).Length

Write-Host ""
Write-Host "DIST: $distRoot"
Write-Host "ZIP:  $zipPath"
Write-Host "Files: $fileCount"
Write-Host "Dist size: $([math]::Round($distSize/1MB, 2)) MB"
Write-Host "ZIP size:  $([math]::Round($zipSize/1MB, 2)) MB"
Write-Host "EXE size:  $([math]::Round((Get-Item $exePath).Length/1MB, 2)) MB"
Write-Host "DIST.1 hygiene: OK"
