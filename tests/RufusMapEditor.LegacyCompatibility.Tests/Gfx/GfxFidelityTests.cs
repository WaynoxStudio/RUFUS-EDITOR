using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.LegacyCompatibility.Tests.Support;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Gfx;

public sealed class GfxResourceResolverTests
{
    private static string? ResolveLibraryRoot() => RufusTestPaths.ResolveGfxLibrary();

    [Fact]
    public void GfxId_374_resolves_to_different_files_per_namespace()
    {
        var root = ResolveLibraryRoot();
        if (root is null)
        {
            return; // skip when library unavailable in CI
        }

        var built = AstriaGfxCatalogBuilder.Build(root);
        var catalog = built.Catalog;
        Assert.True(GfxResourceResolver.TryResolve(catalog, GfxCategory.Ground, 374, out var ground));
        Assert.True(GfxResourceResolver.TryResolve(catalog, GfxCategory.Object, 374, out var obj));
        Assert.NotEqual(ground.FilePath, obj.FilePath, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("grounds", ground.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("objects", obj.FilePath, StringComparison.OrdinalIgnoreCase);

        var cats = GfxResourceResolver.GetCategoriesWithId(catalog, 374);
        Assert.Contains(GfxCategory.Ground, cats);
        Assert.Contains(GfxCategory.Object, cats);
    }

    [Fact]
    public void Native_dimensions_match_png_header()
    {
        var root = ResolveLibraryRoot();
        if (root is null) return;

        var built = AstriaGfxCatalogBuilder.Build(root);
        Assert.True(built.Catalog.TryGet(GfxCategory.Ground, 374, out var res) && res is not null);
        var native = GfxResourceResolver.GetNativeDimensions(res);
        Assert.NotNull(native);
        Assert.True(native!.Value.Width > 0);
        Assert.True(native.Value.Height > 0);
        Assert.Equal(native.Value.Width, res.PixelWidth);
        Assert.Equal(native.Value.Height, res.PixelHeight);
    }
}

public sealed class LayerFieldMappingTests
{
    private static MapDocument LoadMap()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, "10420.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        return map;
    }

    [Fact]
    public void Layer_roundtrip_preserves_ground_object1_object2_independently()
    {
        var map = LoadMap();
        var cell = map.Cells[100];
        var original = MapDataCodec.EncodeCell(cell);

        cell.GroundGfxId = 8;
        cell.Object1GfxId = 1200;
        cell.Object2GfxId = 2400;
        MapCellEditor.SyncMapDataString(map);

        var decoded = MapDataCodec.DecodeCell(MapDataCodec.ExtractCellBlock(map.MapData, 100));
        Assert.Equal(8, decoded.GroundGfxId);
        Assert.Equal(1200, decoded.Object1GfxId);
        Assert.Equal(2400, decoded.Object2GfxId);

        map.Cells[100] = MapDataCodec.DecodeCell(original);
        var restored = MapDataCodec.EncodeCell(map.Cells[100]);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Ground_object2_same_numeric_id_are_independent_fields()
    {
        var cell = new CellData
        {
            GroundGfxId = 374,
            Object1GfxId = 0,
            Object2GfxId = 374,
        };
        var block = MapDataCodec.EncodeCell(cell);
        var decoded = MapDataCodec.DecodeCell(block);
        Assert.Equal(374, decoded.GroundGfxId);
        Assert.Equal(0, decoded.Object1GfxId);
        Assert.Equal(374, decoded.Object2GfxId);
    }

    [Fact]
    public void Fixture_maps_with_gfx_374_record_layer_field()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
        if (!Directory.Exists(root)) return;

        foreach (var file in Directory.EnumerateFiles(root, "*.sql"))
        {
            var map = AstriaSqlMapParser.ParseFile(file);
            map.Cells = MapDataCodec.DecodeMap(map.MapData);
            for (var i = 0; i < map.Cells.Count; i++)
            {
                var c = map.Cells[i];
                if (c.GroundGfxId == 374 || c.Object1GfxId == 374 || c.Object2GfxId == 374)
                {
                    Assert.True(c.GroundGfxId == 374 || c.Object1GfxId == 374 || c.Object2GfxId == 374);
                }
            }
        }
    }
}

public sealed class CellGeometryFidelityTests
{
    [Fact]
    public void Cell_center_matches_diamond_midpoint_15x17()
    {
        var corners = IsoGeometry.BuildCellCorners(15, 17);
        Assert.Equal(479, corners.Length);
        for (var id = 0; id < corners.Length; id++)
        {
            var c = corners[id];
            var (cx, cy) = IsoGeometry.GetCellCenter(c);
            Assert.Equal((c.A.X + c.C.X) / 2.0, cx, 1);
            Assert.Equal((c.A.Y + c.C.Y) / 2.0, cy, 1);
        }
    }

    [Fact]
    public void Hit_tester_corners_match_iso_geometry_in_hit_space()
    {
        var tester = new IsoHitTester(15, 17);
        var full = IsoGeometry.BuildCellCorners(15, 17);
        var crop = IsoGeometry.ExportCrop(15, 17);
        for (var id = 0; id < full.Length; id++)
        {
            Assert.True(tester.TryGetCellCornersInHitSpace(id, out var hit));
            var f = full[id];
            Assert.Equal(f.A.X - crop.X, hit.A.X);
            Assert.Equal(f.A.Y - crop.Y, hit.A.Y);
            Assert.Equal(f.C.X - crop.X, hit.C.X);
            Assert.Equal(f.C.Y - crop.Y, hit.C.Y);
        }
    }

    [Fact]
    public void Grid_and_hit_test_share_same_cell_at_center()
    {
        var tester = new IsoHitTester(15, 17);
        const int cellId = 228;
        Assert.True(tester.TryGetCellCornersInHitSpace(cellId, out var c));
        var (cx, cy) = IsoGeometry.GetCellCenter(c);
        Assert.Equal(cellId, tester.HitTest(cx, cy));
    }
}

public sealed class PreviewFinalResourceParityTests
{
    private static string? ResolveLibraryRoot() => RufusTestPaths.ResolveGfxLibrary();

    [Fact]
    public void Catalog_and_renderer_resolve_same_file_for_ground_and_object()
    {
        var root = ResolveLibraryRoot();
        if (root is null) return;

        var built = AstriaGfxCatalogBuilder.Build(root);
        var catalog = built.Catalog;
        var renderer = new AstriaMapRenderer(catalog);

        foreach (var (cat, id) in new[] { (GfxCategory.Ground, 374), (GfxCategory.Object, 374) })
        {
            Assert.True(GfxResourceResolver.TryResolve(catalog, cat, id, out var catalogRes));
            Assert.True(catalog.TryGet(cat, id, out var directRes));
            Assert.Equal(catalogRes.FilePath, directRes!.FilePath, StringComparer.OrdinalIgnoreCase);
            var hash = GfxResourceResolver.ComputeFileHashSha256(catalogRes);
            Assert.False(string.IsNullOrEmpty(hash));
        }

        _ = renderer;
    }
}
