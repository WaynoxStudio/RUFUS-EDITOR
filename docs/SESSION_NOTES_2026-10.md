# Notas de sesión (oct 2026) — para continuidad agente/humano

Resumen de decisiones y hallazgos que no estaban documentados antes.

## Producto

- **Solo `RufusAdmin.exe`** en escritorio. App = librería embebida.
- Sidebar útil: Mapas | NPC y diálogos | Ajustes (misiones viven dentro de Contenido).
- Raíz tipica limpia: `src`, `Library`, `scripts`, `tests`, `tools`, `docs`, `RufusAdmin.exe`, solución.
- Backup local hecho en: `Desktop\RUFUS EDITOR BACKUP 20261018-120912` (sin artifacts/dist).

## Mapas — “solo carga 1”

- No era que faltaran mapas en disco (`Library\Maps` ~137 carpetas).
- Causa: `settings.json` → `MapListFilter.SearchText = "30021"` (filtro persistente).
- Fix: vaciar búsqueda en UI o resetear ese campo en settings.

## Oeste (Library)

- Assets en `Library\Images\objects\Oeste\` (p. ej. 9102–9132), fondos 632–635.
- Catálogo: `Library\XML\objects.xml`, `grounds.xml`.
- Subido a GitHub `main` (rama de trabajo fusionada).

## Vulkania / maps_es

- Coords BD sincronizadas con isla; `maps_es` debe coincidir en X/Y.
- Duplicados `MA.m` con **coords distintas** = bug (fantasma en mapa mundo).
- Duplicados con **mismas coords** y distinto `sa`/`ep` = patrón histórico válido.
- Pipeline: `ReplaceVariants` + batch + publish remoto; caso publicado hacia `maps,es,1289` (verificar servidor).
- Detalle: [LANG_MAPS_AND_COORDS.md](LANG_MAPS_AND_COORDS.md).

## Admin UX (sesión previa)

- Capas de suelo: comprobar visibilidad “ShowGround” / capas en inspector.
- Arrastre de mapas en mundo: sin exigir Ctrl (drag del mapa vs pan).
- Borrado en drag: no cortar el trazo con `CommitSelection` a destiempo.
- “Cargar BD” sin diálogo de overwrite molesto (según cambio aplicado en App).

## Git

- Remoto: `https://github.com/WaynoxStudio/RUFUS-EDITOR` — default **`main`**.
- Clonar sin rama extra = lo que hay en `main`.

## Reglas Cursor del repo

- Abrir editor → siempre **RufusAdmin** (ver `.cursor/rules/open-rufus-admin.mdc`).
- Import objetos Oeste: no preguntar ID libre si el ancla es clara; reiniciar Admin al terminar.
