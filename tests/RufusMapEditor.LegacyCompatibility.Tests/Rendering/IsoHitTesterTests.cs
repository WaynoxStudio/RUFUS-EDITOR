using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Rendering;

public sealed class IsoHitTesterTests
{
    [Fact]
    public void HitTest_center_of_cell_0_returns_0()
    {
        var tester = new IsoHitTester(15, 17);
        Assert.True(tester.TryGetCellCornersInHitSpace(0, out var c));
        var cx = (c.A.X + c.B.X + c.C.X + c.D.X) / 4.0;
        var cy = (c.A.Y + c.B.Y + c.C.Y + c.D.Y) / 4.0;
        Assert.Equal(0, tester.HitTest(cx, cy));
    }

    [Fact]
    public void HitTest_outside_image_returns_null()
    {
        var tester = new IsoHitTester(15, 17);
        Assert.Null(tester.HitTest(-10, -10));
        Assert.Null(tester.HitTest(5000, 5000));
    }

    [Fact]
    public void HitTest_matches_astria_diamond_test_for_many_cells()
    {
        var tester = new IsoHitTester(15, 17);
        for (var id = 0; id < tester.Corners.Count; id += 17)
        {
            Assert.True(tester.TryGetCellCornersInHitSpace(id, out var c));
            var cx = (c.A.X + c.C.X) / 2.0;
            var cy = (c.B.Y + c.D.Y) / 2.0;
            Assert.True(IsoHitTester.PointInDiamond(cx + 26, cy + 13, tester.Corners[id])); // full canvas
            Assert.Equal(id, tester.HitTest(cx, cy));
        }
    }

    [Fact]
    public void Cell_ids_cover_expected_count_for_15x17()
    {
        var tester = new IsoHitTester(15, 17);
        Assert.Equal(479, tester.Corners.Count);
    }
}
