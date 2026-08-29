# Edición Multimap (Fase 9M.2)

## Resumen

RUFUS Map Editor permite editar varios mapas colocados en MUNDO como un lienzo continuo, **sin fusionar MapData**. Cada mapa conserva su `MapDocument`, Cell IDs locales y serialización independiente.

Esta capacidad es una **extensión RUFUS** — no está demostrada como feature nativa de Astria Map Editor.

## Arquitectura

```
WorldDocument
  └─ WorldMapEntry (por mapa, key GUID)
       └─ MapDocument (MapData propio)
            └─ Cell ID local (0..N-1)
```

### Componentes

| Componente | Ubicación | Rol |
|---|---|---|
| `WorldCellRef` | LegacyCompatibility/World | Par `(DocumentKey, CellId)` |
| `WorldMapHitTest` | App/Services | Puntero mundo → mapa + celda vía `IsoHitTester` |
| `MultiMapEditService` | App/Services | Stroke, selección, undo, guardado |
| `CompositeMapEditCommand` | LegacyCompatibility/Editing | Un Ctrl+Z para N mapas |
| `WorldEditHistory` | LegacyCompatibility/Editing | Pila undo/redo a nivel mundo |

## Flujo de usuario

1. Colocar mapas adyacentes en MUNDO (vista mosaico recomendada).
2. Multiseleccionar mapas (Ctrl+clic / marquee).
3. **Editar selección** → modo `EDICIÓN MULTIMAPA`.
4. Usar herramientas del editor MAPA (catálogo compartido): Pintar, Borrar, Eyedropper, selección rectangular.
5. Un stroke puede cruzar mapas; **Ctrl+Z** deshace todo el stroke.
6. **Guardar mapas modificados** / Ctrl+S persiste `.rufmap` por documento.

## Hit test

Coordenadas mundo → rectángulo preview (`WorldGeometry.GetMapRect`, gap=0 en mosaico) → coordenadas locales → `IsoHitTester.HitTest`.

Paridad verificada: misma celda que abrir el mapa individualmente en MAPA.

Clave de stroke: `{DocumentKey}:{CellId}` — Cell 228 en mapa A ≠ Cell 228 en mapa B.

## Cell-locked painting (9UI.1)

Durante Paint/Erase:

1. Puntero → hit-test iso → **Cell propietaria** (rombo `OverlayPaintTarget*` encima del preview).
2. GFX se coloca con `GfxPlacementMath` + anchor — puede sobresalir del rombo.
3. Preview == Final (9P.2).
4. Cell mostrada == Cell modificada al click.

Orden overlay: rejilla → Cell ID → selección → preview GFX → bounds → **target cell** (encima).

Stroke rápido: interpolación por segmento (`IsoStrokeInterpolation` / `WorldMapHitTest.CellsAlongSegment`) para no saltar celdas.

Status bar multimap: `Map ID | Cell | World X,Y | capa | GFX`.

## Undo transaccional

`BeginStroke` acumula snapshots por documento. `FinishStroke` construye un `CellBatchEditCommand` por mapa afectado y los envuelve en `CompositeMapEditCommand`.

Undo ejecuta `Undo` en orden inverso; Redo re-ejecuta todos.

## Visibilidad

Reutiliza toggles 9UI (`ShowBackgroundLayer`, capas, rejilla, Cell ID). Thumbnails en multimap respetan `MapRenderOptions` del usuario.

Cell ID local por mapa; auto-oculto bajo zoom 35% (`ShowCellIdsEffective`).

## Guardado

- **Guardar mapas modificados**: escribe `.rufmap` en `LinkedRufmapPath` o `%LocalAppData%\RufusMapEditor\world-maps\`.
- **Guardar mundo**: composición `.rufworld` (sin MapData gigante).
- Mapas no seleccionados: visibles atenuados, no editables.

## Copy/Paste

Clipboard multimap con offsets en píxeles mundo. Paste puede distribuir celdas en mapas adyacentes si el hit test encuentra celda destino.

## Replace GFX

Sobre selección multimap o (opcional) todos los mapas editables — una transacción undo.

## Tamaños soportados

| Tamaño | Estado |
|---|---|
| 15×17 (479 cells) | OK — fixture principal |
| 19×22 Grande | PENDIENTE — fórmula `MapGeometry.CellCount` aplica; fixtures limitados |
| Custom (Autre) | PENDIENTE — no creador de mapas en 9M.2 |

## Limitaciones

- Propiedades mixed multimap (Movement/LoS/Interactive en selección cross-map): no implementado — solo edición por capa/GFX.
- Replace en todos los mapas editables (`ApplyMultiMapReplaceInMaps`): API disponible; UI dedicada opcional pendiente.
- Reorganizar mapas bloqueado durante edición multimap.
- Map creator Medio/Grande/Personalizado: fase posterior.

## Catálogo GFX responsive (9UI.1)

Columnas calculadas con `GfxCatalogLayout.ComputeColumns(anchoPanel)` — tile 78px (72+margin). ListBox virtualizado por filas; lazy thumbnails preservados. Recalcula en resize/splitter/UI scale vía `MainViewModel.SetCatalogPanelWidth`.

## Rendimiento

Invalidación incremental de thumbnails solo en mapas modificados (`WorldThumbnailCache.Invalidate`). Overlays no regeneran MapData.
