# RUFUS Licensing (LIC.1–LIC.2)

## Persistencia V1

- **Motor:** SQLite privado del Backend RUFUS
- **Independiente** de BD DOFUS (`estaticos`, mapas, npcs, etc.)
- **Tablas:** `rufus_licenses`, `rufus_devices`, `rufus_sessions`, `rufus_admin_audit`
- **Abstracción:** `ILicenseRepository` / `IDeviceRepository` / `ISessionRepository` / `ILicenseUnitOfWork`
- Migración futura a MySQL/PostgreSQL sin cambiar contratos Editor ni lógica principal

### Ruta SQLite

Prioridad:

1. Env `RUFUS_LICENSE_DB_PATH`
2. Path configurado (relativo al content root del backend o absoluto)
3. Default: `{BaseDirectory}/data/rufus-licenses.db`

- Fuera de `dist\RUFUS Map Editor\`
- No en Git (`*.db`, `**/data/rufus-licenses.db`)
- **Producción VPS (LIC.4 confirmada):** `/home/ubuntu/RufusAiBackend/data/rufus-licenses.db`
  - Usuario del servicio `rufus-ai` (`ubuntu`)
  - Fuera de `/var/www/html`
  - Env: `RUFUS_LICENSE_DB_PATH` en `rufus-ai.env` (mode 600)

### Código de licencia

- Formato: `RUF-XXXX-XXXX-XXXX-XXXX` (Crockford Base32, RNG)
- Servidor guarda solo **SHA-256** del código normalizado + hint últimos 4
- Plaintext se muestra **una vez** al crear (ADMIN); no almacenamiento reversible

### Session token

- Opaco, 32 bytes URL-safe, distinto del license code y de `RUFUS_AI_ACCESS_TOKEN`
- Backend guarda solo hash SHA-256

## Lease / Heartbeat V1

| Parámetro | Default | Env |
|-----------|---------|-----|
| Lease | **900 s (15 min)** | `RUFUS_LICENSE_LEASE_SECONDS` |
| Heartbeat sugerido cliente | **300 s (5 min)** | `RUFUS_LICENSE_HEARTBEAT_SECONDS` |

Hora de validez: **servidor** (`IServerClock`).

## Device ID

- Provider: `WindowsMachineGuidDeviceIdProvider`
- Fuente: `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`
- Enviado: `SHA256("rufus-device-v1\|" + MachineGuid)` hex
- No depende de la carpeta portable → copiar EXE+Library no transfiere identidad
- No envía GUID crudo, MAC, usuario Windows ni series

## Session store (Editor)

- `DpapiLicenseSessionStore` → `%LocalAppData%\RufusMapEditor\license-session.bin`
- Nunca bajo `Library\`
- Sin OpenAI / admin secrets

## Contratos Editor (no desplegados en LIC.2)

| Método | Ruta |
|--------|------|
| Activate | `POST /v1/license/activate` |
| Session/Validate | `POST /v1/license/session` (alias conceptual; Activate también emite sesión) |
| Heartbeat | `POST /v1/license/heartbeat` |
| Logout | `POST /v1/license/logout` |

Base URL futura: `RUFUS_LICENSE_API_BASE` / Admin: `RUFUS_ADMIN_API_BASE`.

**Producción HTTPS (LIC.4):** `https://vmi3502135.contaboserver.net` (proxy Apache `/v1/license/` + `/v1/admin/` → `127.0.0.1:5088`).

## IA

- Hoy: `EnvironmentAiBackendAccessTokenProvider` + `RUFUS_AI_ACCESS_TOKEN` (preservado)
- Preparado: `SessionAccessTokenProvider` → `IAiBackendAccessTokenProvider` (no activado)
- Futuro: Bearer = session token; backend exige `permission.ai`

## RUFUS ADMIN (especificación)

App **privada**, nunca en dist.

### Lista LICENCIAS

Buscar por código/estado. Columnas: Licencia (hint), Estado, Activación, Caducidad, Dispositivos n/max, Sesiones n/max, Editor, IA.

### Detalle + acciones

EXTENDER, SUSPENDER, REACTIVAR, REVOCAR, RESET DISPOSITIVO, CERRAR SESIÓN.

### Crear

Duración (1/3/7/15/30/90/personalizada días), dispositivos default 1, sesiones default 1, Editor sí, IA sí/no, notas.

### Auth ADMIN V1

- Backend: `RUFUS_ADMIN_API_SECRET` (≥16)
- Tool ADMIN: credencial local protegida
- Separada de license codes, session tokens, OpenAI, `RUFUS_AI_ACCESS_TOKEN`
- `IAdminCredentialVerifier` para evolución posterior

### Auditoría

Acciones en `rufus_admin_audit` sin secretos.

### Backup

Copia del fichero SQLite (preferible con backend parado) vía `LicenseSqliteBackup.CopyDatabaseFile`, o manual en VPS:

```bash
sudo systemctl stop rufus-ai
mkdir -p /home/ubuntu/backups/licenses
cp -a /home/ubuntu/RufusAiBackend/data/rufus-licenses.db \
  /home/ubuntu/backups/licenses/rufus-licenses-$(date -u +%Y%m%d-%H%M%S).db
sudo systemctl start rufus-ai
```

No copia `rufus-ai.env` ni secretos. Sin infraestructura compleja en LIC.4.

## Integración Editor LIC.2

**No bloquea** arranque. Solo esqueleto + tests. Enforce = fase posterior.

## LIC.3 — Host local (AiBackend)

Mismo proceso ASP.NET que la IA. Rutas:

| Método | Ruta |
|--------|------|
| POST | `/v1/license/activate` |
| POST | `/v1/license/session` |
| POST | `/v1/license/heartbeat` |
| POST | `/v1/license/logout` |
| POST | `/v1/admin/licenses` |
| GET | `/v1/admin/licenses` |
| GET | `/v1/admin/licenses/{id}` |
| POST | `/v1/admin/licenses/{id}/extend` |
| POST | `/v1/admin/licenses/{id}/suspend` |
| POST | `/v1/admin/licenses/{id}/reactivate` |
| POST | `/v1/admin/licenses/{id}/revoke` |
| POST | `/v1/admin/licenses/{id}/reset-device` |
| POST | `/v1/admin/licenses/{id}/terminate-session` |

Admin: `Authorization: Bearer <RUFUS_ADMIN_API_SECRET>` **antes** del body.

```powershell
$env:RUFUS_ADMIN_API_SECRET = "local-dev-admin-secret-32chars!!"
dotnet run --project src/RufusMapEditor.AiBackend
dotnet run --project src/RufusMapEditor.Admin
dotnet run --project tools/LicenseProbe -- activate --code RUF-...
```

Puerto local: `http://127.0.0.1:5088`. ADMIN y LicenseProbe fuera de dist.

## LIC.4 — Producción VPS

- Host: mismo `RufusMapEditor.AiBackend` + `rufus-ai.service`
- Editor **sin** bloqueo por licencia; IA sigue con `RUFUS_AI_ACCESS_TOKEN`
- ADMIN: Base URL + secret en `%LocalAppData%\RufusMapEditor\admin-connection.bin` (DPAPI). Env `RUFUS_ADMIN_API_BASE` / `RUFUS_ADMIN_API_SECRET` solo como respaldo de primera ejecución. Auto-conexión al arrancar tras conexión válida.

## LIC.5 — Editor OPT-IN (modo prueba)

Único flag:

| Variable | Valores | Default |
|----------|---------|---------|
| `RUFUS_LICENSE_TEST` | `1` / `true` / `yes` / `on` | **OFF** (ausente o cualquier otro valor) |

Base URL cliente: `RUFUS_LICENSE_API_BASE` o, si no está, `https://vmi3502135.contaboserver.net`.

### Activar modo TEST

```powershell
$env:RUFUS_LICENSE_TEST = "1"
dotnet run --project src/RufusMapEditor.App
```

### Desactivar

```powershell
Remove-Item Env:RUFUS_LICENSE_TEST -ErrorAction SilentlyContinue
```

Con TEST **OFF**: arranque idéntico a LIC.4 (sin pantalla, sin heartbeat).

Con TEST **ON**: DeviceId → SessionStore → validar `/v1/license/session` → si no válida, pantalla Activación → heartbeat 300 s / lease 900 s → logout best-effort al salir. IA usa **SessionToken** (sin fallback a `RUFUS_AI_ACCESS_TOKEN`).

## LIC.6 — IA por sesión + cuota

### Auth generate

`POST /v1/ai/generate`:

1. Bearer = SessionToken Licensing (preferente)
2. Validar sesión / lease / licencia / dispositivo / `permission.ai`
3. Cuota diaria/mensual (hora servidor)
4. Solo entonces OpenAI

`OPENAI_API_KEY` solo VPS.

### Legacy token (dev)

| Variable | Default | Efecto |
|----------|---------|--------|
| `RUFUS_AI_LEGACY_TOKEN_ENABLED` | **OFF** | Si `1`/`true`/`yes`/`on` **y** token desconocido como sesión, acepta `RUFUS_AI_ACCESS_TOKEN` |

```powershell
# Activar compatibilidad legacy (solo desarrollo)
$env:RUFUS_AI_LEGACY_TOKEN_ENABLED = "1"
$env:RUFUS_AI_ACCESS_TOKEN = "<token compartido>"

# Desactivar (producción futura)
Remove-Item Env:RUFUS_AI_LEGACY_TOKEN_ENABLED -ErrorAction SilentlyContinue
```

Con `RUFUS_LICENSE_TEST=1` el Editor **nunca** hace fallback silencioso al token compartido.

### Cuotas

- Columnas `ai_daily_limit` / `ai_monthly_limit` (NULL = sin límite)
- Contadores SQLite `rufus_ai_quota_counters` + eventos `rufus_ai_usage_events` (tokens opcionales)
- Consumo al **entrar** a OpenAI (errores OpenAI también cuentan; auth denegada = 0)
- Migración schema v2 additive (no borra licencias)

### ADMIN

`POST /v1/admin/licenses/{id}/ai-settings` · UI «IA / Límites…»

Auditoría: `ai.permission_changed`, `ai.limit_changed`.

## LIC.7 — Enforcement definitivo + builds separadas

### Perfiles

| Perfil | Comando | Licensing | IA auth |
|--------|---------|-----------|---------|
| **USER** (distribución) | `scripts/build-portable-dist.ps1` | **Siempre** (`RUFUS_USER`) | SessionToken |
| **DEVELOPMENT** | `dotnet run --project src/RufusMapEditor.App` | OFF salvo `RUFUS_LICENSE_TEST=1` | Env token o SessionToken si TEST |
| **ADMIN** | `scripts/build-admin-dist.ps1` | N/A | N/A |

`RUFUS_LICENSE_TEST=0` **no desactiva** la build USER.

### Build USER

```powershell
.\scripts\build-portable-dist.ps1
```

Salida: `dist\RUFUS Map Editor\`

### Build ADMIN

```powershell
.\scripts\build-admin-dist.ps1
```

Salida: `dist-admin\RUFUS ADMIN\`

### Desarrollo

```powershell
dotnet run --project src/RufusMapEditor.App
$env:RUFUS_LICENSE_TEST = "1"   # opcional: probar licencia
dotnet run --project src/RufusMapEditor.App
```

### LIC.7P.1 — Cierre vs logout

- **Cerrar aplicación:** detiene heartbeat; **no** llama `/logout`; sesión DPAPI conservada.
- **Cerrar sesión** (Ajustes → Licencia): logout backend + limpia store.
- Bootstrap: `ShutdownMode=OnExplicitShutdown` hasta mostrar hub; evita shutdown al cerrar activación.

### LIC.7P.2 — Estado visible

- **Ajustes → Licencia:** estado, `licenseExpiresAt` (backend), tiempo restante (solo display), dispositivo, permisos, cuotas IA.
- **Caducidad en UI:** campo API `licenseExpiresAt` → `DateTimeOffset.ToLocalTime()` solo para presentación (`dd/MM/yyyy HH:mm`). No se calcula desde duración local.
- **Tiempo restante / “caduca pronto”:** informativos; no autorizan ni bloquean (autoridad = backend).
- **Actualizar estado:** revalida vía heartbeat/`/session` existente; refleja extensión ADMIN y cambios `permission.ai` / cuotas.
- **Hub:** indicador discreto `Licencia: Activa · N días restantes` (o `Estado no actualizado` si falla red).
- **Sin datos sensibles:** no SessionToken, DeviceId, MachineGuid, URLs, secrets.
