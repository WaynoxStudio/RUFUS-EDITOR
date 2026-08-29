# Preview → Commit → Final Placement Parity (Fase 9G.2)

**Fecha:** 2026-08-22  
**Mapa de reproducción:** 10439 (árbol / Object grande)  
**Estado:** corregido en pipeline común; sin offsets mágicos ni hacks por GfxID.

---

## Bug manual

Antes del click, el preview semitransparente del pincel parecía anclado a la Cell objetivo.
Después del click, el GFX final en `MapImage` y/o el rombo de selección no coincidían visualmente con ese preview.

Los tests antiguos `PreviewToFinalPlacementTests` solo comprobaban que **la misma fórmula**
`GfxPlacementMath.CalculateDrawPlacement` devolvía el mismo rectángulo cuando se le
pasaban los mismos inputs — no el path real UI:

`MouseMove hover → preview overlay → MouseDown hit → PaintCell → Rerender → MapImage`.

---

## Causa raíz

1. **Compositing WPF vs GDI (primaria):** `BitmapConversion.ToBitmapSource` usaba
   `HorizontalResolution`/`VerticalResolution` del bitmap GDI+. El overlay del pincel
   congela siempre a **96 DPI**. Con `MapImage.Stretch=None`, un DPI ≠ 96 desplaza el
   contenido final respecto a rombos/preview del `OverlayCanvas` (coordenadas en píxeles).
2. **Hover vs click:** `MouseDown` recalculaba el hit-test sin refrescar `HoveredCellId`
   con el mismo punto. Preview usaba hover de `MouseMove`; paint usaba un hit independiente.
3. **Clip:** el preview WPF podía dibujar overhang con `Canvas.Top/Left` negativos fuera del
   bitmap exportado; el final está cropeado a export space. `OverlayCanvas.ClipToBounds=True`
   alinea el recorte visual.

La geometría Astria (`anchor → centro del rombo`) **no** se cambió. No se “recentró” el pie del sprite.

---

## Pipeline corregido

```
Mouse / Hover
  → IsoHitTester.HitTest          (único)
  → Target CellId
  → GfxResource (category + id)
  → native W/H (bitmap real)
  → ResolveAnchor (XML Pos | centro imagen)
  → Flip / Rotation efectiva
  → GfxPlacementPipeline.TryBuild → GfxPlacementDescriptor
       ├─ FullCanvas (Draw_Tile)
       └─ HitSpace = Full − ExportCrop (una sola vez)
  → Preview: OverlayCanvas Image @ HitSpace (opacidad ~55%)
  → Commit: MapCellEditor.SetLayerGfx
  → Final: AstriaMapRenderer.DrawTile @ FullCanvas → crop export
```

Fuente única: `GfxPlacementPipeline` + `GfxPlacementMath` (sin `PreviewPlacementMath` paralelo).

---

## Caso 10439 (regresión)

Fixture de test: Map 10439, Cell 228, Object `670` (`Images/objects/Arbres/670.png`),
Capa 1, Flip=0, Rot=0.

- Preview descriptor == Committed descriptor (geometría idéntica).
- Pixel sample: sprite preview vs renderer final (blend sobre negro) mismatch=0.

El GfxID del árbol en la sesión manual del usuario puede diferir (pintado sin guardar);
la regresión fija un árbol grande representativo del mismo workflow.

---

## Tests 9G.2

`PreviewCommitParityTests`:

- descriptor sintético
- Map 10439 paint Object1
- transforms flip/rot
- anchor negativo
- **todos** los Ground cargables
- **todos** los Object únicos cargables
- XML ambiguos → primera entrada (Astria `Get_*_Pos`)
- pixel parity sample
- crop una sola vez (15×17 → 26,13,728,416)
- celdas borde

---

## Política Astria

- Anchor XML puede no coincidir con el “pie visual”.
- Preview correcto = Final correcto = Astria `Draw_Tile`.
- Si Final == Astria y Preview ≠ Final → se corrige Preview/inputs UI, no se inventan offsets.
