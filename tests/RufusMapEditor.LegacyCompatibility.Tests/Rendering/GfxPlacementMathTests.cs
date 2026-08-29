using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Rendering;

public sealed class GfxPlacementMathTests
{
    private static IsoGeometry.CellCorners SampleCell() =>
        IsoGeometry.BuildCellCorners(15, 17)[228];

    [Fact]
    public void Bounds_uses_real_image_size_not_cell_size()
    {
        var cell = SampleCell();
        var bounds = GfxPlacementMath.ComputeBounds(
            cell,
            imageWidth: 200,
            imageHeight: 300,
            anchorX: 10,
            anchorY: 20,
            flip: false,
            rotation: 0,
            isObject: true);

        Assert.Equal(200, bounds.Width);
        Assert.Equal(300, bounds.Height);
        Assert.NotEqual(IsoGeometry.SizeBaseCell, bounds.Width);
    }

    [Fact]
    public void Bounds_object_flip_adjusts_x_like_astria()
    {
        var cell = SampleCell();
        var normal = GfxPlacementMath.ComputeBounds(cell, 120, 80, 30, 40, false, 0, true);
        var flipped = GfxPlacementMath.ComputeBounds(cell, 120, 80, 30, 40, true, 0, true);
        Assert.NotEqual(normal.X, flipped.X);
    }

    [Fact]
    public void Bounds_ground_flip_does_not_adjust_x()
    {
        var cell = SampleCell();
        var normal = GfxPlacementMath.ComputeBounds(cell, 120, 80, 30, 40, false, 0, false);
        var flipped = GfxPlacementMath.ComputeBounds(cell, 120, 80, 30, 40, true, 0, false);
        Assert.Equal(normal.X, flipped.X);
    }

    [Fact]
    public void Bounds_rotation_1_uses_post_rotate_dimensions()
    {
        var cell = SampleCell();
        var rot1 = GfxPlacementMath.ComputeBounds(cell, 100, 200, 20, 30, false, 1, true);
        var expectedW = (int)Math.Ceiling(200 / 100.0 * 192.86);
        var expectedH = (int)Math.Ceiling(100 / 100.0 * 51.85);
        Assert.Equal(expectedW, rot1.Width);
        Assert.Equal(expectedH, rot1.Height);
    }

    [Fact]
    public void Bounds_object2_rotation_zero_only()
    {
        var cell = SampleCell();
        var rot0 = GfxPlacementMath.ComputeBounds(cell, 100, 200, 20, 30, false, 0, true);
        var rot3 = GfxPlacementMath.ComputeBounds(cell, 100, 200, 20, 30, false, 3, true);
        Assert.NotEqual(rot0.X, rot3.X);
    }

    [Fact]
    public void Hit_space_conversion_subtracts_export_crop()
    {
        var cell = SampleCell();
        var full = GfxPlacementMath.ComputeBounds(cell, 64, 64, 8, 8, false, 0, true);
        var crop = IsoGeometry.ExportCrop(15, 17);
        var hit = full.ToHitSpace(crop.X, crop.Y);
        Assert.Equal(full.X - crop.X, hit.X);
        Assert.Equal(full.Y - crop.Y, hit.Y);
        Assert.Equal(full.Width, hit.Width);
    }

    [Fact]
    public void ResolveAnchor_defaults_to_center_when_missing()
    {
        var (x, y) = GfxPlacementMath.ResolveAnchor(null, null, 100, 50);
        Assert.Equal(50, x);
        Assert.Equal(25, y);
    }

    [Fact]
    public void Preview_transform_matches_bounds_dimensions_for_rotation0()
    {
        var cell = SampleCell();
        using var bmp = new System.Drawing.Bitmap(80, 120);
        using var transformed = GfxPlacementMath.TransformBitmap(bmp, 12, 18, false, 0, true, IsoGeometry.SizeBaseCell, out var logical);
        var bounds = GfxPlacementMath.ComputeBounds(cell, 80, 120, 12, 18, false, 0, true);
        Assert.Equal(logical.Width, bounds.Width);
        Assert.Equal(logical.Height, bounds.Height);
        Assert.Equal(80, transformed.Width);
        Assert.Equal(120, transformed.Height);
    }
}
