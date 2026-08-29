# Formato `.rufmap` (RUFUS Map Editor)

## Objetivo

`.rufmap` es el **formato nativo editable** de RUFUS Map Editor: un proyecto de trabajo autocontenido para abrir, editar y recuperar mapas sin escribir SQL ni SWF de DOFUS.

No sustituye:

- export SQL;
- SWF / Flasm;
- `.ame`.

La instalación Astria permanece **solo lectura**. Los GFX no se empaquetan en el `.rufmap`; se resuelven desde la biblioteca configurada.

## Versión

| Campo | Valor actual |
|-------|----------------|
| `formatVersion` | **1** |

La versión del formato es independiente de la versión del ejecutable.  
Arquitectura de migraciones: `RufmapMigrator.MigrateToCurrent` (hoy no-op para v1; punto de extensión v1→v2→…).

Si `formatVersion` es **mayor** que la soportada, la carga **falla por completo** (sin carga parcial).

## Estructura (JSON, UTF-8)

Preferencia: `System.Text.Json`. No se usa `BinaryFormatter`. DTOs en `RufusMapEditor.LegacyCompatibility.Rufmap` (sin tipos WPF).

```text
{
  formatVersion,      // int — obligatorio
  documentId,         // GUID string — identidad del proyecto (NO es Map ID)
  createdUtc,
  modifiedUtc,
  projectName?,       // metadata de proyecto local
  comment?,
  source?: {          // origen histórico (no dependencia de apertura)
    kind,             // "LegacyAstria" | "RufmapNative" | …
    originalMapId?,
    libraryPathHint?  // hint; el .rufmap no requiere que exista
  },
  map: {
    id, width, height,
    dateMap, key, fightPlaces,
    backgroundId, musicId, ambianceId, capabilities, outdoor?,
    cells: [ { …campos MapData… } ],  // CANÓNICO
    mapData               // referencia de integridad (Encode(cells) al guardar)
  }
}
```

### Campos de celda (`cells[]`)

Todos los campos respaldados por MapData clásico:

`active`, `los`, `movement`, `ground`, `object1`, `object2`,  
`flipG`, `flipO1`, `flipO2`, `rotG`, `rotO1`, `level`, `slope`, `io`.

No se serializan fight cells, triggers de script, houses, mobs, etc. (fuera de alcance).

## Canónico vs derivado vs metadata

| Dato | Rol |
|------|-----|
| `map.cells` | **Fuente canónica** del estado editable |
| `map.mapData` | **Derivado / integridad**: debe coincidir con `Encode(cells)` al guardar; se verifica al cargar |
| `map.width/height/id/…` | Metadata de mapa (documento) |
| `source.*` | Origen histórico (informativo) |
| `documentId` | Identidad local del proyecto / autosave |
| `projectName`, fechas, `comment` | Metadata de proyecto local (no servidor DOFUS) |

No se guardan en `.rufmap`: favoritos GFX, recientes de UI, tamaño de paneles, ruta de biblioteca activa (van a `%LocalAppData%\RufusMapEditor\settings.json`).

## Recursos GFX

Solo se persisten **GfxID** (y categoría implícita por capa).  
Si falta un recurso al abrir: aviso **«Recurso ausente»** (RESOURCE_GAP), sin crash.

## Guardado atómico

1. Escribir `destino.rufmap.tmp` completo + flush.
2. Rechazar temp de 0 bytes.
3. `File.Replace(temp, destino, destino.bak)` si ya existía; si no, `Move`.
4. Un solo `.bak` (política: no acumular backups infinitos).

Si falla el replace, el `.rufmap` anterior permanece válido.

## Autosave y recuperación

- Ubicación: `%LocalAppData%\RufusMapEditor\autosave\`
- Archivos: `{documentId}.rufmap.autosave` + `{documentId}.meta.json`
- **No** sobrescribe el `.rufmap` principal.
- Intervalo por defecto: **120 s** (`settings.AutosaveIntervalSeconds`, mínimo efectivo 30).
- Solo si el documento está **dirty** y no hay stroke abierto.
- Autosave **no** llama a `MarkClean` → dirty permanece true.
- Tras Guardar manual correcto: se elimina el autosave de ese `documentId`.
- Al arrancar: diálogo Recuperar / Descartar / Ignorar (no recuperación silenciosa).
- Documentos nunca guardados (import Astria editado) también generan autosave.

## Compatibilidad legacy

Movement se guarda como entero raw (0–7), no como `Cell.Type()` de Astria.  
Roundtrip esperado: legacy SQL → celdas → `.rufmap` → Encode MapData ≡ MapData original (fixtures 30/30).

## Extensión de archivo

La extensión `.rufmap` **no** basta: se valida JSON, `formatVersion`, `documentId` y coherencia width/height/celdas/MapData.
