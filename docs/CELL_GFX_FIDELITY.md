# FASE 9G — Cell Geometry / Layer Fidelity / GFX Resource Chain

**Fecha:** 2026-08-22  
**Referencia Astria (solo lectura):** `_refs/AstriaMapEditor` + instalación local `Astria Map Editor 1`  
**Estado:** completada en código y tests automatizados; comparación visual manual Astria↔RUFUS recomendada en mapa real del usuario.

---

## 1. Cadena inequívoca (Layer + GfxID → píxel final)

```
MapDocument
  → CellData (GroundGfxId / Object1GfxId / Object2GfxId + flip/rot)
  → MapDataCodec (10 chars/celda, golden fixtures intactos)
  → Inspector / Brush (GfxCategory + GfxID)
  → GfxResourceResolver.TryResolve(category, id)
  → GfxResource (FilePath, Anchor, PixelWidth/Height)
  → GfxPlacementMath (Draw_Tile / SurRound)
  → AstriaMapRenderer (final) | GfxOverlayCache + MapViewport (preview)
```

**Regla:** nunca resolver solo por `GfxID` numérico cuando el namespace importa.

---

## 2. Geometría de celdas (Astria confirmado)

| Concepto | Astria (`MapEditor.vb`) | RUFUS |
|---|---|---|
| `SizeBaseCell` | 26 | `IsoGeometry.SizeBaseCell = 26` |
| Grid | `GenerateGrid()` doble bucle | `IsoGeometry.BuildCellCorners` |
| Rombo | A(top), B(right), C(bottom), D(left) | `MakeDiamond` idéntico |
| Centro celda | implícito en dibujo | `IsoGeometry.GetCellCenter` = midpoint A↔C |
| Hit test | `Get_IdCell` cross-product 4 lados | `IsoHitTester.HitTest` |
| Espacio export | `Save_Img` / `RogneImage` crop | `IsoGeometry.ExportCrop` → hit space vía `-crop` |
| 15×17 celdas | 479 | `MapGeometry.CellCount(15,17)=479` ✓ |
| 19×22 celdas | 796 (Astria fórmula) | `MapGeometry.CellCount(19,22)=796` (sin fixture visual dedicado) |

**Centralización 9G:** grid, hover, paint target, Cell ID y debug overlay usan `IsoHitTester.TryGetCellCornersInHitSpace` → derivado de `IsoGeometry` + crop único. No hay fórmula paralela en grid vs hit-test.

**Crop / doble offset:** el renderer aplica `ExportCrop` una vez al bitmap; el hit-test opera en espacio recortado (`exportCroppedSpace=true`). No se aplica crop dos veces en overlays.

---

## 3. Mapeo de capas MapData ↔ Astria ↔ RUFUS

| Astria campo | RUFUS `CellData` | Inspector | Catálogo |
|---|---|---|---|
| `Gfx1` | `GroundGfxId` | Suelo | `GfxCategory.Ground` |
| `Gfx2` | `Object1GfxId` | Capa 1 | `GfxCategory.Object` |
| `Gfx3` | `Object2GfxId` | Capa 2 | `GfxCategory.Object` |
| Background | `BackgroundGfxId` (map-level) | Fondo | `GfxCategory.Background` |

Codec verificado: encode/decode roundtrip independiente por capa (`LayerFieldMappingTests`).

**Object2:** sin rotación en MapData (Astria `RotaGfx3` no usado en SurRound) — respetado.

---

## 4. Namespaces GFX y GfxID 374 (caso de reproducción)

Existen **recursos distintos** con el mismo número:

| Namespace | GfxID 374 | Archivo (Astria library) |
|---|---|---|
| Ground | 374 | `Images/grounds/Nowel/374.png` |
| Object | 374 | `Images/objects/Végétation/374.png` |

**No hay regla global** `374 = Capa X`. El campo serializado (`GroundGfxId` vs `Object1GfxId` vs `Object2GfxId`) + `GfxCategory` desambigua.

Si el inspector muestra GfxID 374 en **Suelo**, es correcto **si y solo si** `GroundGfxId==374` en MapData. La línea de detalle del inspector ahora indica dimensiones, carpeta, anchor y aviso `ID en Ground/Object` cuando el número existe en varios namespaces.

**Fixtures SQL:** ninguno de los 30 fixtures contiene 374 en MapData — la reproducción del usuario requiere su mapa concreto (pendiente comparación celda a celda con Astria).

---

## 5. Dimensiones nativas y thumbnails

- `AstriaGfxCatalogBuilder` rellena `PixelWidth`/`PixelHeight` al indexar.
- `GfxImageDimensions` lee header PNG/JPEG/BMP como fallback.
- `GfxResourceResolver.GetNativeDimensions` expone W×H reales (no tamaño del control WPF).
- Inspector: detalle compacto `86×113 px · carpeta · anchor X,Y · ID en Ground/Object`.
- Catálogo / inspector previews: `Stretch=Uniform` (aspect ratio preservado).
- Brush preview overlay: `Stretch=Fill` **intencional** — rellena el `PlacementRect` ya calculado por `GfxPlacementMath` (distinto contexto que thumbnail).

---

## 6. Preview == Final

- Misma fuente: `IGfxCatalog.TryGet(category, id)` + mismo anchor del namespace.
- Misma geometría: `GfxPlacementPipeline` → `GfxPlacementMath.CalculateDrawPlacement` (full) + `ToHitSpace` (overlay).
- Opacidad ~55% / bounds / rombo de selección son las únicas diferencias visuales del preview.
- **Fase 9G.2:** `MapImage` a 96 DPI + `Stretch=Fill`; `MouseDown` refresca hover antes de paint;
  `OverlayCanvas.ClipToBounds=True`. Ver `docs/PREVIEW_COMMIT_PARITY.md`.
- Tests: `PreviewToFinalPlacementTests` + `PreviewCommitParityTests` (catálogo completo) +
  `PreviewFinalResourceParityTests`.

---

## 7. Debug overlay (solo `ShowDebugInfo`)

En `MapViewport`, con debug activo y celda bajo cursor:

- Vértices A–D (lime), centro (cyan), punto hit-test (yellow ring)
- Texto: Cell ID, center, bounding rect
- Misma geometría que grid/hover/paint target

Multimap / World: Cell ID usa `IsoGeometry.GetCellCenter` + offset de mapa en mundo.

---

## 8. Tests 9G añadidos (`GfxFidelityTests.cs`)

| Test | Verifica |
|---|---|
| `GfxId_374_resolves_to_different_files_per_namespace` | Ground ≠ Object path |
| `Native_dimensions_match_png_header` | PixelWidth/Height vs header |
| `Layer_roundtrip_preserves_*` | Encode/decode por capa |
| `Ground_object2_same_numeric_id_*` | Campos independientes |
| `Cell_center_matches_diamond_midpoint_15x17` | Centro vs A↔C |
| `Hit_tester_corners_match_iso_geometry_in_hit_space` | Crop consistente |
| `Grid_and_hit_test_share_same_cell_at_center` | Hit en centro → misma celda |
| `Catalog_and_renderer_resolve_same_file_*` | Paridad lookup + hash |

**Total suite:** suite productiva RUFUS (YACO reference removed; not a RUFUS source of truth).

---

## 9. Problemas reportados — causas probables

| Síntoma | Causa más probable | Acción 9G |
|---|---|---|
| GFX en Suelo “incorrecto” | ID numérico compartido Ground/Object; datos correctos pero confusión UX | Detalle inspector + nota overlap |
| Preview distinto al catálogo | Escala thumbnail vs placement rect completo | Documentado; preview usa bitmap real en placement |
| Celda desalineada | Fórmulas divergentes (pre-9G) | Centralizado en IsoGeometry + hit space |
| Dimensiones wrong en UI | Metadata no poblada | PixelWidth/Height en build + inspector detail |

---

## 10. Manual checklist (usuario)

1. Mapa 15×17: activar grid + Cell IDs; recorrer bordes/centro — rombo hover = rombo lógico.
2. GfxID 374: abrir misma celda en Astria; comparar campo Gfx1/Gfx2/Gfx3 vs inspector RUFUS.
3. Ground pequeño / Object grande: catálogo → inspector → preview → render sin deformación de aspect ratio en thumbs.
4. Flip / Rot Ground / Rot Object1: barra superior = estado aplicado; eyedropper copia transformaciones.

---

## 11. Archivos tocados (9G)

- `IsoGeometry.cs` — `GetCellCenter`
- `GfxResourceResolver.cs`, `GfxImageDimensions.cs` — lookup + dims
- `AstriaGfxCatalogBuilder.cs` — pixel metadata
- `MainViewModel.cs` — `CellInfo*Detail`
- `MainWindow.xaml` — bindings detalle capas
- `MapViewport.xaml.cs` — debug overlay, hit point tracking
- `WorldViewport.xaml.cs` — Cell ID center unificado
- `tests/.../Gfx/GfxFidelityTests.cs`
