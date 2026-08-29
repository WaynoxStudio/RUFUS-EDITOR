# RufusMapEditor.AiBackend (AI.4B)

Backend aislado del WPF. Único componente autorizado a hablar con OpenAI.

## Arquitectura

```
RUFUS Editor (IAiGenerationService)
        ↓  POST  (contrato AI.4A)
RufusMapEditor.AiBackend
        ↓  Responses API + Structured Outputs
OpenAI
```

## Ruta interna

`POST /v1/ai/generate`

`GET /health` — estado + si hay `OPENAI_API_KEY` (sin revelar la clave).

## Variables de entorno

| Variable | Obligatorio | Default |
|---|---|---|
| `OPENAI_API_KEY` | para generar | (vacío → `AI_NOT_CONFIGURED`) |
| `OPENAI_MODEL` | no | `gpt-5-mini` |
| `RUFUS_AI_ACCESS_TOKEN` | para aceptar llamadas del Editor | (vacío → 401 en `/v1/ai/generate`) |

- `OPENAI_API_KEY` **solo** en el VPS/backend. Nunca en el Editor ni en git.
- `RUFUS_AI_ACCESS_TOKEN` es un secreto **distinto** (Editor ↔ Backend). Misma variable en ambos lados en esta fase.

El Editor envía:

`Authorization: Bearer <RUFUS_AI_ACCESS_TOKEN>`

## AI.6C — autenticación Editor ↔ Backend

Middleware `RufusAiGenerateAuthMiddleware` valida `Authorization: Bearer` **antes** de leer el body.

Sin token / token incorrecto → `401 UNAUTHORIZED` (OpenAI no se llama), aunque el JSON esté vacío o corrupto.
Token correcto + JSON inválido → `400 INVALID_REQUEST`.
Apache `Require ip` **no** se elimina todavía en esta fase.

## Arranque local (desarrollo)

```powershell
$env:OPENAI_API_KEY = "..."              # solo backend / VPS
$env:RUFUS_AI_ACCESS_TOKEN = "..."       # mismo valor en backend y Editor
$env:OPENAI_MODEL = "gpt-5-mini"
dotnet run --project src/RufusMapEditor.AiBackend
```

Puerto local de desarrollo: ver `Properties/launchSettings.json` (solo local).

## AI.6B.2 — Editor ↔ backend VPS HTTPS (temporal)

El Editor resuelve `BackendUrl` desde:

1. Variable de entorno `RUFUS_AI_BACKEND_URL` (opcional), o
2. Endpoint VPS temporal AI.6B.2:
   `https://vmi3502135.contaboserver.net/v1/ai/generate`

Durante esta fase **no** se usa `127.0.0.1:5088` desde el Editor.
No hace falta arrancar `RufusMapEditor.AiBackend` en el PC.
La API key permanece solo en el VPS.

```powershell
$env:RUFUS_AI_ACCESS_TOKEN = "..."   # requerido para generar
dotnet run --project src/RufusMapEditor.App
```

En Contenido → ASISTENTE IA el estado debe mostrar **IA: Disponible**.
Sin token, Generar muestra: *No autorizado para utilizar el servicio IA de RUFUS.*


## Prueba OpenAI real (manual)

```powershell
dotnet run --project tools/AiOpenAiProbe -- --real
```

Sin `--real` no llama a OpenAI.
