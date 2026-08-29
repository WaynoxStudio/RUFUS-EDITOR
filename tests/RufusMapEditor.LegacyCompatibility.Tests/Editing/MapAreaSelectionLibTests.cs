using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Editing;

/// <summary>MAP-AREA.1 — mass selection fill/clear + unique Cell ID set semantics.</summary>
public sealed class MapAreaSelectionLibTests
{
    private static MapDocument TinyMap(int cells = 20)
    {
        var map = new MapDocument
        {
            Width = 5,
            Height = 5,
            Cells = Enumerable.Range(0, cells)
                .Select(_ => new CellData
                {
                    GroundLevel = 7,
                    GroundSlope = 1,
                    LineOfSight = true,
                    Movement = MovementType.Walkable,
                    GroundGfxId = 10,
                    Object1GfxId = 20,
                    Object2GfxId = 30,
                })
                .ToList(),
        };
        map.MapData = MapDataCodec.EncodeMap((IReadOnlyList<CellData>)map.Cells);
        return map;
    }

    [Fact]
    public void Union_of_regions_deduplicates_overlapping_cell_ids()
    {
        var a = new HashSet<int> { 1, 2, 3, 4, 5 };
        var b = new HashSet<int> { 4, 5, 6, 7 };
        a.UnionWith(b);
        Assert.Equal(7, a.Count);
        Assert.DoesNotContain(8, a);
    }

    [Fact]
    public void Fill_selection_ground_batch_is_one_undo_and_preserves_other_layers()
    {
        var map = TinyMap();
        var ids = new[] { 0, 1, 2, 3, 4 };
        var history = new EditHistory();

        var cmd = CellBatchEditCommand.Build("Rellenar selección", map, ids,
            (_, c) => MapCellEditor.SetLayerGfx(c, MapCellEditor.Layer.Ground, 458, flip: false, rotation: 0));
        Assert.NotNull(cmd);
        Assert.Equal(5, cmd!.ChangeCount);
        history.PushExecuted(cmd);

        foreach (var id in ids)
        {
            Assert.Equal(458, map.Cells[id].GroundGfxId);
            Assert.Equal(20, map.Cells[id].Object1GfxId);
            Assert.Equal(30, map.Cells[id].Object2GfxId);
        }

        Assert.Equal(10, map.Cells[5].GroundGfxId); // outside selection

        Assert.True(history.Undo(map));
        foreach (var id in ids)
            Assert.Equal(10, map.Cells[id].GroundGfxId);

        Assert.True(history.Redo(map));
        foreach (var id in ids)
            Assert.Equal(458, map.Cells[id].GroundGfxId);
    }

    [Fact]
    public void Clear_object2_only_in_selection_leaves_ground_and_object1()
    {
        var map = TinyMap();
        var ids = new[] { 2, 3, 4 };
        var history = new EditHistory();

        var cmd = CellBatchEditCommand.Build("Vaciar Capa 2 (selección)", map, ids,
            (_, c) => MapCellEditor.ClearLayer(c, MapCellEditor.Layer.Object2));
        Assert.NotNull(cmd);
        history.PushExecuted(cmd!);

        foreach (var id in ids)
        {
            Assert.Equal(0, map.Cells[id].Object2GfxId);
            Assert.Equal(10, map.Cells[id].GroundGfxId);
            Assert.Equal(20, map.Cells[id].Object1GfxId);
        }

        Assert.Equal(30, map.Cells[0].Object2GfxId); // outside

        Assert.True(history.Undo(map));
        foreach (var id in ids)
            Assert.Equal(30, map.Cells[id].Object2GfxId);
    }

    [Fact]
    public void Clear_ground_and_object1_independently()
    {
        var map = TinyMap();
        var ids = new[] { 1, 2 };

        CellBatchEditCommand.Build("Vaciar Suelo", map, ids,
            (_, c) => MapCellEditor.ClearLayer(c, MapCellEditor.Layer.Ground));
        Assert.Equal(0, map.Cells[1].GroundGfxId);
        Assert.Equal(20, map.Cells[1].Object1GfxId);

        CellBatchEditCommand.Build("Vaciar Capa 1", map, ids,
            (_, c) => MapCellEditor.ClearLayer(c, MapCellEditor.Layer.Object1));
        Assert.Equal(0, map.Cells[1].Object1GfxId);
        Assert.Equal(30, map.Cells[1].Object2GfxId);
    }

    [Fact]
    public void Large_selection_fill_single_history_entry()
    {
        var map = TinyMap(200);
        var ids = Enumerable.Range(0, 150).ToArray();
        var history = new EditHistory();
        var cmd = CellBatchEditCommand.Build("Rellenar selección", map, ids,
            (_, c) => MapCellEditor.SetLayerGfx(c, MapCellEditor.Layer.Object1, 99, false, 0));
        Assert.NotNull(cmd);
        Assert.Equal(150, cmd!.ChangeCount);
        history.PushExecuted(cmd);
        Assert.Equal(1, history.UndoCount);
        Assert.True(history.Undo(map));
        Assert.Equal(20, map.Cells[0].Object1GfxId);
        Assert.True(history.Redo(map));
        Assert.Equal(99, map.Cells[0].Object1GfxId);
    }

    [Fact]
    public void Rect_geometry_selection_still_works()
    {
        var tester = new IsoHitTester(15, 17);
        Assert.True(tester.TryGetCellCornersInHitSpace(0, out var c0));
        var (cx, cy) = ((c0.A.X + c0.C.X) / 2.0, (c0.B.Y + c0.D.Y) / 2.0);
        var set = IsoSelection.CellsIntersectingRect(tester, cx - 5, cy - 5, cx + 5, cy + 5);
        Assert.Contains(0, set);
    }
}
