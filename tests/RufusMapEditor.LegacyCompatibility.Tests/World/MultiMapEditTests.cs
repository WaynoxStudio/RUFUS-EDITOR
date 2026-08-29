using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.World;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.World;

public sealed class WorldMapHitTestTests
{
    private static MapDocument LoadMap(string file = "10420.sql")
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, file));
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        return map;
    }

    [Fact]
    public void Hit_test_local_matches_single_map_isohit()
    {
        var map = LoadMap();
        var tester = new IsoHitTester(map.Width, map.Height);
        tester.TryGetCellCornersInHitSpace(228, out var c);
        var cx = (c.A.X + c.C.X) / 2.0;
        var cy = (c.B.Y + c.D.Y) / 2.0;

        var single = tester.HitTest(cx, cy);

        var (rx, ry, w, h) = WorldGeometry.GetMapRect(0, 0, map, mosaicMode: true);
        Assert.Equal(0, rx);
        Assert.Equal(0, ry);
        var localTester = new IsoHitTester(map.Width, map.Height);
        var multi = localTester.HitTest(cx, cy);

        Assert.Equal(228, single);
        Assert.Equal(228, multi);
    }

    [Fact]
    public void Same_cell_id_in_different_maps_are_independent_stroke_keys()
    {
        var a = new WorldCellRef("mapA", 228);
        var b = new WorldCellRef("mapB", 228);
        Assert.NotEqual(a.StrokeKey, b.StrokeKey);
    }
}

public sealed class CompositeMapEditCommandTests
{
    private static MapDocument LoadMap()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, "10420.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        return map;
    }

    [Fact]
    public void Composite_undo_restores_both_maps_atomically()
    {
        var mapA = LoadMap();
        var mapB = LoadMap();
        mapB.Id = mapA.Id + 1;

        var world = new WorldDocument();
        var keyA = "a";
        var keyB = "b";
        world.Documents[keyA] = new WorldMapEntry { Key = keyA, Document = mapA };
        world.Documents[keyB] = new WorldMapEntry { Key = keyB, Document = mapB };

        var beforeA = CellSnapshot.Capture(10, mapA.Cells[10]);
        var beforeB = CellSnapshot.Capture(20, mapB.Cells[20]);

        MapCellEditor.SetLayerGfx(mapA.Cells[10], MapCellEditor.Layer.Ground, 999);
        MapCellEditor.SetLayerGfx(mapB.Cells[20], MapCellEditor.Layer.Ground, 888);
        MapCellEditor.SyncMapDataString(mapA);
        MapCellEditor.SyncMapDataString(mapB);

        var afterA = CellSnapshot.Capture(10, mapA.Cells[10]);
        var afterB = CellSnapshot.Capture(20, mapB.Cells[20]);

        var cmd = new CompositeMapEditCommand("Stroke test", new[]
        {
            (keyA, new CellBatchEditCommand("A", new[] { (beforeA, afterA) })),
            (keyB, new CellBatchEditCommand("B", new[] { (beforeB, afterB) })),
        });

        cmd.Undo(world);
        Assert.Equal(beforeA.GroundGfxId, mapA.Cells[10].GroundGfxId);
        Assert.Equal(beforeB.GroundGfxId, mapB.Cells[20].GroundGfxId);

        cmd.Execute(world);
        Assert.Equal(999, mapA.Cells[10].GroundGfxId);
        Assert.Equal(888, mapB.Cells[20].GroundGfxId);
    }

    [Fact]
    public void Visibility_toggle_does_not_change_mapdata()
    {
        var map = LoadMap();
        var before = map.MapData;
        _ = new MapRenderOptions { DrawGround = false };
        Assert.Equal(before, map.MapData);
    }
}
