# RUFUS Map Editor — Módulo MUNDO (Fase 9M.1)

## Resumen

El módulo **MUNDO** permite componer visualmente muchos mapas completos sobre una cuadrícula X/Y, con previews reales, duplicación profunda, Map IDs locales y persistencia `.rufworld`.

Dos espacios de trabajo en la UI: pestañas **MAPA** y **MUNDO**.

---

## Arquitectura

| Capa | Componentes |
|---|---|
| Domain | `WorldDocument`, `WorldMapPlacement`, `WorldMapEntry`, `WorldViewState`, `WorldMapOrigin`, `WorldMapPublicationState` |
| LegacyCompatibility | `WorldEditorService`, `MapDocumentDuplicator`, `LocalMapIdAllocator`, `WorldGeometry`, `RufworldSerializer`, `RufworldIo`, `AstriaGeoImporter` |
| App | `WorldViewModel`, `WorldViewport`, `WorldThumbnailCache`, `WorldAutosaveStore` |

**Fuente de verdad lógica:** `WorldEditorService` (sin WPF).

**Geometría world↔pantalla:** `WorldGeometry` + `WorldMapHitTest` (9M.2) — `WorldMapEntry` → `MapDocument` → celda local.

**Previews:** `WorldThumbnailCache` llama a `AstriaLibraryService.Render` (mismo renderer que MAPA). Cache por fingerprint de `MapData`.

---

## Formato `.rufworld` (v1)

- JSON versionado (`formatVersion: 1`)
- Documentos embebidos (misma estructura de mapa que `.rufmap`)
- Placements `{ documentKey, x, y }`
- `unplacedKeys` — bandeja de mapas sin posición
- `view` — zoom, pan, mosaicMode, showInfoOverlay
- Sin rutas absolutas obligatorias; sin thumbnails embebidos
- Guardado atómico vía `RufworldIo.SaveAtomic`

---

## Coordenadas

- Enteras, positivas y negativas
- Canvas dinámico según bounds de mapas colocados (sin límite 10×10)
- Cada celda = un mapa completo (dimensiones desde `MapDocument.Width/Height`)

---

## Map ID local

- Interfaz `ILocalMapIdAllocator` / `LocalMapIdAllocator`
- Algoritmo: `sourceId + 1`, avanzar mientras esté en reservados (mundo + biblioteca cargada)
- Estado `LocalUnpublished` — **ID local pendiente de validar en BD** (Fase 10)
- Entrada manual en diálogo al duplicar

---

## Duplicación

`MapDocumentDuplicator.DeepCopy` — copia profunda de celdas, metadata, MapData; nuevo `Id`.

Posición: adyacente libre (preferencia derecha) vía `WorldGeometry.FindAdjacentFree`.

**Quitar del Mundo ≠ borrar archivo.**

---

## Interacción (9M.1)

- Zoom: rueda; pan: middle / Space+LMB
- Selección simple, Ctrl+clic, marquee
- Drag mapa → nueva celda (intercambio si ocupada)
- Doble clic / Enter → abrir en MAPA (misma instancia `MapDocument`)
- Al volver a MUNDO: zoom/pan conservados; preview invalidada tras edición
- Vista mosaico: gap UI = 0 entre previews
- Copiar/pegar = duplicar contenido a nueva posición
- Autosave: `%LocalAppData%\RufusMapEditor\world-autosave\`

---

## Import Astria `.geo` (solo lectura)

`AstriaGeoImporter` — BinaryFormatter `CellGeo[]`, campos `MapID`, `x_pos`, `y_pos`.

Genera mundo con mapas cargados desde biblioteca en esas coordenadas.

---

## Edición multimap (9M.2 — COMPLETADO)

Ver **`docs/MULTIMAP_EDITING.md`**.

- Modo **Editar selección** en MUNDO
- Paint/Erase/Eyedropper cross-map con `MapCellEditor`
- Undo transaccional `CompositeMapEditCommand`
- Hit test `WorldMapHitTest` → `IsoHitTester` (paridad MAPA)
- Guardar mapas modificados → `.rufmap` independientes

**Extensión RUFUS** — no demostrada en Astria original.

---

## Aplazado (Fase 10)

- BD / publicación / Navicat
- Generación geopositions servidor
- Maisons/enclos, monstres, préparation patch, auto-placement triggers
- Map creator Medio/Grande/Personalizado

---

## Tests

`tests/.../World/WorldEditorTests.cs` — duplicación, Map ID, posiciones, save/load, mosaico.

`tests/.../World/MultiMapEditTests.cs` — hit test paridad, composite undo, visibilidad.

---

## CONFIRMADO vs PENDIENTE

| Tema | Estado |
|---|---|
| Grid X/Y Astria `Geoposition.vb` | CONFIRMADO (referencia) |
| `.geo` BinaryFormatter | CONFIRMADO (import read-only) |
| `.rufworld` RUFUS | CONFIRMADO |
| Area/SubArea en UI Astria | CONFIRMADO selectores `Area.Areas` / `SubArea.SubAreas` — **semántica servidor RUFUS: PENDIENTE** |
| Générer geopositions / maisons / monstres / patch | Documentado Astria — **NO implementado** |
