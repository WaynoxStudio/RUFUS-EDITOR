using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.App.Services;

public enum EditorTool
{
    Select = 0,
    RectSelect = 1,
    Paint = 2,
    Erase = 3,
    Eyedropper = 4,
    Unwalkable = 5,
    LineOfSight = 6,
    FightCell1 = 7,
    FightCell2 = 8,
    /// <summary>LIB.4.1 — select a cell for fixed-mob group (does not mutate walkable/LoS/fight).</summary>
    MobCell = 9,
}

public static class EditorToolExtensions
{
    public static bool IsCellModeTool(this EditorTool tool) =>
        tool is EditorTool.Unwalkable or EditorTool.LineOfSight or EditorTool.FightCell1 or EditorTool.FightCell2;
}

public enum PaintLayer
{
    Ground = MapCellEditor.Layer.Ground,
    Object1 = MapCellEditor.Layer.Object1,
    Object2 = MapCellEditor.Layer.Object2,
}

public static class PaintLayerExtensions
{
    public static MapCellEditor.Layer ToEditorLayer(this PaintLayer layer) =>
        (MapCellEditor.Layer)(int)layer;

    public static GfxCategory ToGfxCategory(this PaintLayer layer) =>
        layer == PaintLayer.Ground ? GfxCategory.Ground : GfxCategory.Object;
}

public sealed class GfxFavoriteKey
{
    public required string Category { get; init; }
    public required int GfxId { get; init; }
}

public sealed class GfxRecentEntry
{
    public required string Category { get; init; }
    public required int GfxId { get; init; }
}

/// <summary>Inspector-driven highlight of a placed GFX layer on the map (distinct from cell diamond).</summary>
public enum InspectorLayerHighlight
{
    None = 0,
    Ground = 1,
    Object1 = 2,
    Object2 = 3,
}
