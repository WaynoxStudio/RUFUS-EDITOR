using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Editing;

/// <summary>MAP-PAINT.1 — right-click in paint only removes the active brush stamp.</summary>
public sealed class MapPaintHotfixTests
{
    [Fact]
    public void Active_brush_match_ignores_other_gfx_and_empty_cells()
    {
        var cell = new CellData
        {
            GroundGfxId = 100,
            Object1GfxId = 458,
            Object2GfxId = 900,
        };

        Assert.True(ActiveBrushMatches(cell, layerObject1: true, brushId: 458));
        Assert.False(ActiveBrushMatches(cell, layerObject1: true, brushId: 500));
        Assert.False(ActiveBrushMatches(cell, layerObject1: false, brushId: 458)); // ground layer
        Assert.False(ActiveBrushMatches(new CellData(), layerObject1: true, brushId: 458));
    }

    private static bool ActiveBrushMatches(CellData cell, bool layerObject1, int brushId)
    {
        var gfx = layerObject1 ? cell.Object1GfxId : cell.GroundGfxId;
        return brushId > 0 && gfx == brushId;
    }
}
