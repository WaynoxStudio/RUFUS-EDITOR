using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Rendering;

/// <summary>
/// Preview overlay and AstriaMapRenderer must share identical placement math.
/// </summary>
public sealed class PreviewToFinalPlacementTests
{
    private const string DefaultAstriaRoot = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1";

    private static string? ResolveAstriaRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ASTRIA_MAP_EDITOR_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;
        return Directory.Exists(DefaultAstriaRoot) ? DefaultAstriaRoot : null;
    }

    private static IsoGeometry.CellCorners SampleCell(int cellId = 228) =>
        IsoGeometry.BuildCellCorners(15, 17)[cellId];

    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(true, 2, true)]
    [InlineData(false, 0, false)]
    [InlineData(true, 2, false)]
    public void Preview_and_final_share_CalculateDrawPlacement(bool flip, int rotation, bool isObject)
    {
        var cell = SampleCell();
        const int w = 180;
        const int h = 240;
        const int ax = 42;
        const int ay = -18;

        var placement = GfxPlacementMath.CalculateDrawPlacement(cell, w, h, ax, ay, flip, rotation, isObject);
        var bounds = GfxPlacementMath.ComputeBounds(cell, w, h, ax, ay, flip, rotation, isObject);
        Assert.Equal(bounds, placement);
    }

    [Fact]
    public void Rotation1_uses_post_rotate_dimensions_not_original()
    {
        var cell = SampleCell();
        var fixedPlacement = GfxPlacementMath.CalculateDrawPlacement(cell, 100, 200, 20, 30, false, 1, true);

        var buggyW = (int)Math.Ceiling(100 / 100.0 * 192.86);
        var correctW = (int)Math.Ceiling(200 / 100.0 * 192.86);
        Assert.Equal(correctW, fixedPlacement.Width);
        Assert.NotEqual(buggyW, fixedPlacement.Width);
    }

    [Fact]
    public void Hit_space_placement_matches_full_canvas_then_crop()
    {
        const int mapW = 15;
        const int mapH = 17;
        const int cellId = 100;
        var ok = GfxPlacementMath.TryCalculateDrawPlacementInHitSpace(
            mapW, mapH, cellId, 120, 90, 10, 15, flip: false, rotation: 0, isObject: true,
            out var hitDirect);
        Assert.True(ok);

        var corners = IsoGeometry.BuildCellCorners(mapW, mapH);
        var full = GfxPlacementMath.CalculateDrawPlacement(
            corners[cellId], 120, 90, 10, 15, false, 0, true);
        var crop = IsoGeometry.ExportCrop(mapW, mapH);
        var hitViaCrop = full.ToHitSpace(crop.X, crop.Y);
        Assert.Equal(hitViaCrop, hitDirect);
    }

    [Fact]
    public void TransformBitmap_logical_size_matches_CalculateDrawPlacement()
    {
        var cell = SampleCell();
        using var bmp = new System.Drawing.Bitmap(80, 140);
        using var transformed = GfxPlacementMath.TransformBitmap(
            bmp, 12, 18, flip: true, rotation: 1, isObject: true, IsoGeometry.SizeBaseCell, out var logical);
        var placement = GfxPlacementMath.CalculateDrawPlacement(
            cell, 80, 140, 12, 18, flip: true, rotation: 1, isObject: true);
        Assert.Equal(logical.Width, placement.Width);
        Assert.Equal(logical.Height, placement.Height);
        _ = transformed;
    }

    [Theory]
    [InlineData(LayerKind.Ground)]
    [InlineData(LayerKind.Object1)]
    [InlineData(LayerKind.Object2)]
    public void Real_map_cell_preview_equals_final_for_layer(LayerKind layerKind)
    {
        var root = ResolveAstriaRoot();
        if (root is null) return;

        var built = AstriaGfxCatalogBuilder.Build(root);
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, "Maps", "10421", "10421.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();

        const int cellId = 228;
        var cell = map.Cells[cellId];
        var (gfxId, flip, rotation, isObject) = layerKind switch
        {
            LayerKind.Ground => (cell.GroundGfxId, cell.FlipGround, cell.GroundRotation, false),
            LayerKind.Object1 => (cell.Object1GfxId, cell.FlipObject1, cell.Object1Rotation, true),
            _ => (cell.Object2GfxId, cell.FlipObject2, 0, true),
        };
        if (gfxId <= 0) return;

        var category = isObject ? GfxCategory.Object : GfxCategory.Ground;
        if (!built.Catalog.TryGet(category, gfxId, out var resource) || resource is null)
            return;

        using var cache = new CachedBitmapGfxProvider();
        if (!cache.TryGetBitmap(resource, out var bmp))
            return;

        var (ax, ay) = GfxPlacementMath.ResolveAnchor(resource.Anchor?.X, resource.Anchor?.Y, bmp.Width, bmp.Height);
        var corners = IsoGeometry.BuildCellCorners(map.Width, map.Height);
        var final = GfxPlacementMath.CalculateDrawPlacement(
            corners[cellId], bmp.Width, bmp.Height, ax, ay, flip, rotation, isObject);

        Assert.True(GfxPlacementMath.TryCalculateDrawPlacementInHitSpace(
            map.Width, map.Height, cellId, bmp.Width, bmp.Height, ax, ay, flip, rotation, isObject,
            out var preview));
        var crop = IsoGeometry.ExportCrop(map.Width, map.Height);
        var previewFull = new GfxPlacementMath.PlacementRect(
            preview.X + crop.X, preview.Y + crop.Y, preview.Width, preview.Height);
        Assert.Equal(0, final.DeltaX(previewFull));
        Assert.Equal(0, final.DeltaY(previewFull));
    }

    [Fact]
    public void Large_object2_fixture_10421_cell228_preview_delta_zero()
    {
        var root = ResolveAstriaRoot();
        if (root is null) return;

        var built = AstriaGfxCatalogBuilder.Build(root);
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, "Maps", "10421", "10421.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();
        var cell = map.Cells[228];
        if (cell.Object2GfxId <= 0) return;
        if (!built.Catalog.TryGet(GfxCategory.Object, cell.Object2GfxId, out var res) || res is null)
            return;

        using var cache = new CachedBitmapGfxProvider();
        if (!cache.TryGetBitmap(res, out var bmp)) return;

        var (ax, ay) = GfxPlacementMath.ResolveAnchor(res.Anchor?.X, res.Anchor?.Y, bmp.Width, bmp.Height);
        var corners = IsoGeometry.BuildCellCorners(map.Width, map.Height);
        var final = GfxPlacementMath.CalculateDrawPlacement(
            corners[228], bmp.Width, bmp.Height, ax, ay, cell.FlipObject2, 0, isObject: true);

        Assert.True(GfxPlacementMath.TryCalculateDrawPlacementInHitSpace(
            map.Width, map.Height, 228, bmp.Width, bmp.Height, ax, ay, cell.FlipObject2, 0, true,
            out var preview));

        var crop = IsoGeometry.ExportCrop(map.Width, map.Height);
        var previewFull = new GfxPlacementMath.PlacementRect(
            preview.X + crop.X, preview.Y + crop.Y, preview.Width, preview.Height);
        Assert.Equal(0, final.DeltaX(previewFull));
        Assert.Equal(0, final.DeltaY(previewFull));
    }

    public enum LayerKind
    {
        Ground,
        Object1,
        Object2,
    }
}
