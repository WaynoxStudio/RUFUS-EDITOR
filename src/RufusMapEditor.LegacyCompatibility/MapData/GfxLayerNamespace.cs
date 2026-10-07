using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.MapData;

/// <summary>
/// Ground and Object share numeric IDs but are distinct namespaces.
/// Moving a GFX id across namespaces would silently retarget a different image.
/// </summary>
public static class GfxLayerNamespace
{
    public static GfxCategory CategoryOf(MapCellEditor.Layer layer) =>
        layer == MapCellEditor.Layer.Ground ? GfxCategory.Ground : GfxCategory.Object;

    public static bool SharesNamespace(MapCellEditor.Layer source, MapCellEditor.Layer target) =>
        CategoryOf(source) == CategoryOf(target);

    /// <summary>
    /// Moves GFX between layers only when both belong to the same category namespace.
    /// Object1 ↔ Object2 is allowed; Ground ↔ Object is refused.
    /// </summary>
    public static bool TryMoveGfxToLayer(
        CellData cell,
        MapCellEditor.Layer source,
        MapCellEditor.Layer target,
        int gfxId)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (gfxId <= 0 || source == target)
            return false;
        if (!SharesNamespace(source, target))
            return false;
        if (GetLayerGfx(cell, source) != gfxId)
            return false;

        var flip = GetFlip(cell, source);
        var rot = GetRotation(cell, source);
        if (target == MapCellEditor.Layer.Object2)
            MapCellEditor.SetLayerGfx(cell, target, gfxId, flip);
        else
            MapCellEditor.SetLayerGfx(cell, target, gfxId, flip, rot);
        MapCellEditor.ClearLayer(cell, source);
        return true;
    }

    private static int GetLayerGfx(CellData cell, MapCellEditor.Layer layer) => layer switch
    {
        MapCellEditor.Layer.Ground => cell.GroundGfxId,
        MapCellEditor.Layer.Object1 => cell.Object1GfxId,
        _ => cell.Object2GfxId,
    };

    private static bool GetFlip(CellData cell, MapCellEditor.Layer layer) => layer switch
    {
        MapCellEditor.Layer.Ground => cell.FlipGround,
        MapCellEditor.Layer.Object1 => cell.FlipObject1,
        _ => cell.FlipObject2,
    };

    private static int GetRotation(CellData cell, MapCellEditor.Layer layer) => layer switch
    {
        MapCellEditor.Layer.Ground => cell.GroundRotation,
        MapCellEditor.Layer.Object1 => cell.Object1Rotation,
        _ => 0,
    };
}
