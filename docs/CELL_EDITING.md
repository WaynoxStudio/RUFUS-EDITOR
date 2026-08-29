# Cell editing — Movement, LoS, Fight cells

## Movement (MapData char 2, bits 3–5)

| Raw | Astria name | RUFUS label |
|-----|-------------|-------------|
| 0 | UNWALKABLE | No transitable |
| 1 | DOOR | Puerta |
| 2 | TRIGGER | Trigger |
| 3 | — | Raw sin significado confirmado |
| 4 | WALKABLE | Transitable |
| 5 | PADDOCK | Enclos |
| 6 | — | Raw sin significado confirmado |
| 7 | PATH | Camino |

RUFUS stores raw 3-bit values (does not use Astria `Cell.Type()`).

### No transitable tool

- Sets `Movement = 0`
- Clears `FightCell = 0`
- Does **not** modify `LineOfSight`

## Line of sight (MapData char 0, bit 0x01)

- `LineOfSight = true` → permite visión
- `LineOfSight = false` → **bloquea** visión
- Independent from Movement

UI tool: **Bloquear visión** (left click blocks, right click restores).

## Fight cells (SQL `places`, not MapData)

- `FightCell = 0` — ninguna
- `FightCell = 1` — Equipo 1
- `FightCell = 2` — Equipo 2

Format: `<team1_encoded_ids>|<team2_encoded_ids>` (2 chars per cell ID).

Rules (Astria-compatible):

- Cannot place fight on unwalkable cells (paint + inspector)
- Marking unwalkable clears fight
- Team 1 and 2 are mutually exclusive on the same cell
- Fight + LoS block allowed (no fixture golden; allowed by model)

## Golden fixtures (10421)

| Cell | Property | Value |
|------|----------|-------|
| 10 | Movement 0, LoS true | `Hhaaeaaaaa` |
| 228 | Movement 0, LoS false | `GhaaeaaGpM` |
| 405 | Movement 4, LoS false | `GhGaeaaaaa` |
| 67 | FightCell 1 | from `places` |
| 330 | FightCell 2 | from `places` |

## Overlays

Toggle via **Ver → Vista de celdas** or toolbar.

Render order (approximate): unwalkable → LoS block → fight markers.

| Overlay | Visual |
|---------|--------|
| No transitable | Rojo semitransparente + cruz |
| Bloqueo LoS | Azul claro semitransparente + rombo interior |
| Equipo 1 | Rojo más sólido + “1” |
| Equipo 2 | Azul más sólido + “2” |

Brushes: `OverlayUnwalkable*`, `OverlayLosBlock*`, `OverlayFight1*`, `OverlayFight2*` (theme).

## Map export limit

Viewport image is already crop-space (`ExportCrop`). **Límite del mapa** draws a rectangle at `(0,0)`–`(width,height)` of the rendered bitmap — no double offset.

15×17 @ 26: full 780×442 → crop 26,13,728×416  
19×22 @ 26: full 988×572 → crop 26,13,936×546
