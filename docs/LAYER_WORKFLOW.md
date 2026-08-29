# Layer workflow — GFX vs Combate

## Two different “1 / 2” concepts

| UI | Meaning | Storage |
|----|---------|---------|
| **C1 / C2** | GFX Capa 1 / Capa 2 (objects) | `Object1GfxId` / `Object2GfxId` |
| **E1 / E2** | Combate Equipo 1 / 2 | `FightCell` + SQL `places` |

Status bar shows `GFX: Capa 1` vs `Combate: Equipo 1` — never bare `1`.

## GFX layers (Astria-confirmed)

| Astria | RUFUS | Catalog |
|--------|-------|---------|
| Sol → Gfx1 | Ground | Suelos |
| Calque 1 → Gfx2 | Object1 | Objetos |
| Calque 2 → Gfx3 | Object2 | Objetos |

**Cross-category NOT confirmed:** Ground tiles cannot be assigned to Object1/2 in Astria editor. RUFUS does not implement Ground→Capa1/2.

**C1 ↔ C2:** switching preserves `SelectedGfxId` when both use `GfxCategory.Object`.

**Ground ↔ Object:** changing namespace clears selection (protected).

## Catalog

Tree shows both roots:

- Suelos
- Objetos

Selecting a root syncs paint target category. Ground data and MapData encoding remain intact.

## Depth vs characters

MapHandler.as: Object2 depth = `cellIndex * 100` vs Ground/Object1 = `cellIndex`. Character z-order not defined in Astria reference repo.

**Tooltips:** Capa 1 / Capa 2 only (no “detrás/delante del personaje”).

## Paint workflow

1. Select GFX from catalog (Suelos or Objetos)
2. Choose **C1** or **C2** in sidebar (or paint Ground via Suelos + paint tool)
3. **B** paint tool applies brush to active layer

Cell tools (no transitable, bloquear visión, E1/E2) use the same undo/stroke engine as other cell edits.
