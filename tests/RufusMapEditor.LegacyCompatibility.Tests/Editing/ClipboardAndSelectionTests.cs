using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Editing;

public sealed class ClipboardAndSelectionTests
{
    private static (double X, double Y) Center(IsoHitTester tester, int id)
    {
        Assert.True(tester.TryGetCellCornersInHitSpace(id, out var c));
        return ((c.A.X + c.C.X) / 2.0, (c.B.Y + c.D.Y) / 2.0);
    }

    [Fact]
    public void Paste_preserves_relative_geometry_and_all_fields()
    {
        var cellCount = MapGeometry.CellCount(15, 17);
        var map = new MapDocument
        {
            Width = 15,
            Height = 17,
            Cells = Enumerable.Range(0, cellCount)
                .Select(_ => new CellData { GroundLevel = 7, GroundSlope = 1, LineOfSight = true, Movement = MovementType.Walkable })
                .ToList(),
        };
        map.MapData = MapDataCodec.EncodeMap((IReadOnlyList<CellData>)map.Cells);

        map.Cells[10].GroundGfxId = 11;
        map.Cells[10].Object1GfxId = 22;
        map.Cells[10].FlipObject1 = true;
        map.Cells[10].Object1Rotation = 2;
        map.Cells[10].Movement = MovementType.Path;
        map.Cells[20].GroundGfxId = 33;
        map.Cells[20].InteractiveObject = true;

        var tester = new IsoHitTester(15, 17);
        var clip = MapClipboard.Capture(
            new[] { 10, 20 },
            id => CellSnapshot.Capture(id, map.Cells[id]),
            id => Center(tester, id));
        Assert.NotNull(clip);
        Assert.Equal(2, clip!.Entries.Count);

        const int destId = 50;
        var (dx, dy) = Center(tester, destId);
        var pasted = 0;
        foreach (var entry in clip.Entries)
        {
            var target = IsoSelection.ResolvePasteTarget(tester, dx + entry.OffsetX, dy + entry.OffsetY);
            if (target is null)
                continue;
            entry.Snapshot.ApplyTo(map.Cells[target.Value]);
            pasted++;
        }

        Assert.Equal(2, pasted);
        Assert.Contains(map.Cells, c =>
            c.GroundGfxId == 11 && c.Object1GfxId == 22 && c.FlipObject1 && c.Object1Rotation == 2 && c.Movement == MovementType.Path);
        Assert.Contains(map.Cells, c => c.GroundGfxId == 33 && c.InteractiveObject);
    }

    [Fact]
    public void Rect_selection_uses_geometry_not_id_range()
    {
        var tester = new IsoHitTester(15, 17);
        Assert.True(tester.TryGetCellCornersInHitSpace(0, out var c0));
        var (cx, cy) = ((c0.A.X + c0.C.X) / 2.0, (c0.B.Y + c0.D.Y) / 2.0);
        var set = IsoSelection.CellsIntersectingRect(tester, cx - 5, cy - 5, cx + 5, cy + 5);
        Assert.Contains(0, set);
        Assert.DoesNotContain(400, set);
    }

    [Fact]
    public void Paste_outside_skips_without_throw()
    {
        var tester = new IsoHitTester(15, 17);
        Assert.Null(IsoSelection.ResolvePasteTarget(tester, -1000, -1000, maxDist: 5));
    }

    [Fact]
    public void Replace_gfx_only_matching_layer_in_selection()
    {
        var map = new MapDocument
        {
            Cells = new List<CellData>
            {
                new() { Object1GfxId = 21, GroundGfxId = 1 },
                new() { Object1GfxId = 21, GroundGfxId = 2 },
                new() { Object1GfxId = 99, GroundGfxId = 3 },
            },
        };
        map.MapData = MapDataCodec.EncodeMap((IReadOnlyList<CellData>)map.Cells);

        var cmd = CellBatchEditCommand.Build("Reemplazar GFX", map, new[] { 0, 1, 2 }, (_, c) =>
        {
            if (c.Object1GfxId == 21)
                c.Object1GfxId = 45;
        });
        Assert.NotNull(cmd);
        Assert.Equal(2, cmd!.ChangeCount);
        Assert.Equal(45, map.Cells[0].Object1GfxId);
        Assert.Equal(45, map.Cells[1].Object1GfxId);
        Assert.Equal(99, map.Cells[2].Object1GfxId);
        Assert.Equal(1, map.Cells[0].GroundGfxId);
    }
}
