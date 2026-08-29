using System.Drawing;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.Tests.Support;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Rendering;

/// <summary>
/// Fase 9G.2 — Preview → Commit → Final placement parity (shared descriptor pipeline).
/// </summary>
public sealed class PreviewCommitParityTests
{
    private static string? Lib() => RufusTestPaths.ResolveGfxLibrary();

    private static MapDocument Load10439(string root)
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, "Maps", "10439", "10439.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();
        return map;
    }

    [Fact]
    public void Pipeline_preview_equals_final_for_synthetic_cell()
    {
        var cell = IsoGeometry.BuildCellCorners(15, 17)[228];
        var full = GfxPlacementMath.CalculateDrawPlacement(cell, 120, 200, 40, -10, true, 1, true);
        Assert.True(GfxPlacementPipeline.TryBuild(
            15, 17, 228,
            new GfxResource
            {
                Id = 1,
                Category = GfxCategory.Object,
                FilePath = "x",
                Folder = "t",
                Extension = ".png",
                Anchor = new GfxAnchor(40, -10),
            },
            120, 200, true, 1, true,
            out var d));
        Assert.Equal(full.X, d.DrawXFull);
        Assert.Equal(full.Y, d.DrawYFull);
        Assert.Equal(full.Width, d.DrawWidth);
        Assert.Equal(full.Height, d.DrawHeight);
    }

    [Fact]
    public void Map_10439_paint_object_preview_equals_committed()
    {
        var root = Lib();
        if (root is null) return;

        var built = AstriaGfxCatalogBuilder.Build(root);
        var map = Load10439(root);
        const int cellId = 228;
        const int gfxId = 670; // Arbres — large enough for overhang

        if (!built.Catalog.TryGet(GfxCategory.Object, gfxId, out var resource) || resource is null)
            return;

        using var imgs = new CachedBitmapGfxProvider();
        if (!imgs.TryGetBitmap(resource, out var bmp))
            return;

        Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
            map.Width, map.Height, cellId, resource, bmp,
            flip: false, rotation: 0, isObject: true, out var preview));

        MapCellEditor.SetLayerGfx(map.Cells[cellId], MapCellEditor.Layer.Object1, gfxId, false, 0);
        Assert.Equal(gfxId, map.Cells[cellId].Object1GfxId);

        Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
            map.Width, map.Height, cellId, resource, bmp,
            map.Cells[cellId].FlipObject1, map.Cells[cellId].Object1Rotation, true,
            out var committed));

        Assert.True(preview.GeometryEquals(committed));
        Assert.Equal(cellId, committed.CellId);
        Assert.Equal(GfxCategory.Object, committed.Category);
        Assert.True(committed.UsedXmlAnchor);
    }

    [Fact]
    public void Object2_forces_rotation_zero_in_descriptor_inputs()
    {
        var root = Lib();
        if (root is null) return;
        var built = AstriaGfxCatalogBuilder.Build(root);
        if (!built.Catalog.TryGet(GfxCategory.Object, 80, out var resource) || resource is null)
            return;
        using var imgs = new CachedBitmapGfxProvider();
        if (!imgs.TryGetBitmap(resource, out var bmp)) return;

        Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
            15, 17, 100, resource, bmp, false, rotation: 0, isObject: true, out var d0));
        Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
            15, 17, 100, resource, bmp, false, rotation: 2, isObject: true, out var d2));
        // Callers must pass 0 for Object2; pipeline itself does not invent Object2 policy.
        Assert.False(d0.GeometryEquals(d2));
        Assert.Equal(0, d0.Rotation);
        Assert.Equal(2, d2.Rotation);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public void Transform_matrix_preview_equals_final(bool flip, int rotation)
    {
        var root = Lib();
        if (root is null) return;
        var built = AstriaGfxCatalogBuilder.Build(root);
        if (!built.Catalog.TryGet(GfxCategory.Object, 670, out var resource) || resource is null)
            return;
        using var imgs = new CachedBitmapGfxProvider();
        if (!imgs.TryGetBitmap(resource, out var bmp)) return;

        Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
            15, 17, 228, resource, bmp, flip, rotation, true, out var preview));

        var corners = IsoGeometry.BuildCellCorners(15, 17);
        var (ax, ay) = GfxPlacementMath.ResolveAnchor(resource.Anchor?.X, resource.Anchor?.Y, bmp.Width, bmp.Height);
        var final = GfxPlacementMath.CalculateDrawPlacement(
            corners[228], bmp.Width, bmp.Height, ax, ay, flip, rotation, true);
        Assert.Equal(final.X, preview.DrawXFull);
        Assert.Equal(final.Y, preview.DrawYFull);
        Assert.Equal(final.Width, preview.DrawWidth);
        Assert.Equal(final.Height, preview.DrawHeight);
    }

    [Fact]
    public void Negative_anchor_parity()
    {
        Assert.True(GfxPlacementPipeline.TryBuild(
            15, 17, 50,
            new GfxResource
            {
                Id = 9,
                Category = GfxCategory.Object,
                FilePath = "x",
                Folder = "t",
                Extension = ".png",
                Anchor = new GfxAnchor(-12, -40),
            },
            80, 120, false, 0, true,
            out var d));
        var cell = IsoGeometry.BuildCellCorners(15, 17)[50];
        var full = GfxPlacementMath.CalculateDrawPlacement(cell, 80, 120, -12, -40, false, 0, true);
        Assert.Equal(full.X, d.DrawXFull);
        Assert.Equal(full.Y, d.DrawYFull);
    }

    [Fact]
    public void All_ground_resources_descriptor_parity()
    {
        var root = Lib();
        if (root is null) return;
        var built = AstriaGfxCatalogBuilder.Build(root);
        using var imgs = new CachedBitmapGfxProvider();
        var ok = 0;
        var total = 0;
        foreach (var res in built.Catalog.Enumerate(GfxCategory.Ground))
        {
            if (!imgs.TryGetBitmap(res, out var bmp)) continue;
            total++;
            Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
                15, 17, 228, res, bmp, false, 0, false, out var preview));
            var corners = IsoGeometry.BuildCellCorners(15, 17);
            var (ax, ay) = GfxPlacementMath.ResolveAnchor(res.Anchor?.X, res.Anchor?.Y, bmp.Width, bmp.Height);
            var final = GfxPlacementMath.CalculateDrawPlacement(
                corners[228], bmp.Width, bmp.Height, ax, ay, false, 0, false);
            Assert.Equal(0, final.DeltaX(preview.FullCanvas));
            Assert.Equal(0, final.DeltaY(preview.FullCanvas));
            Assert.Equal(final.Width, preview.DrawWidth);
            Assert.Equal(final.Height, preview.DrawHeight);
            ok++;
        }

        Assert.True(total > 100, $"Expected many grounds, got {total}");
        Assert.Equal(total, ok);
    }

    [Fact]
    public void All_unique_object_resources_descriptor_parity()
    {
        var root = Lib();
        if (root is null) return;
        var built = AstriaGfxCatalogBuilder.Build(root);
        using var imgs = new CachedBitmapGfxProvider();
        var ok = 0;
        var total = 0;
        foreach (var res in built.Catalog.Enumerate(GfxCategory.Object))
        {
            if (!imgs.TryGetBitmap(res, out var bmp)) continue;
            total++;
            Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
                15, 17, 228, res, bmp, false, 0, true, out var preview));
            var corners = IsoGeometry.BuildCellCorners(15, 17);
            var (ax, ay) = GfxPlacementMath.ResolveAnchor(res.Anchor?.X, res.Anchor?.Y, bmp.Width, bmp.Height);
            var final = GfxPlacementMath.CalculateDrawPlacement(
                corners[228], bmp.Width, bmp.Height, ax, ay, false, 0, true);
            Assert.Equal(0, final.DeltaX(preview.FullCanvas));
            Assert.Equal(0, final.DeltaY(preview.FullCanvas));
            Assert.Equal(final.Width, preview.DrawWidth);
            Assert.Equal(final.Height, preview.DrawHeight);
            ok++;
        }

        Assert.True(total > 1000, $"Expected many objects, got {total}");
        Assert.Equal(total, ok);
    }

    [Fact]
    public void Ambiguous_xml_anchors_use_first_entry_consistently()
    {
        var root = Lib();
        if (root is null) return;
        var objXml = GfxAnchorXmlParser.ParseFile(
            Path.Combine(root, "XML", "objects.xml"), GfxCategory.Object);
        if (objXml.AmbiguousAnchorsById.Count == 0) return;

        var built = AstriaGfxCatalogBuilder.Build(root);
        using var imgs = new CachedBitmapGfxProvider();
        foreach (var (id, entries) in objXml.AmbiguousAnchorsById.Take(10))
        {
            if (!built.Catalog.TryGetObject(id, out var res) || res is null) continue;
            if (!imgs.TryGetBitmap(res, out var bmp)) continue;
            Assert.True(res.AnchorAmbiguous);
            Assert.Equal(entries[0].X, res.Anchor!.Value.X);
            Assert.Equal(entries[0].Y, res.Anchor!.Value.Y);
            Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
                15, 17, 10, res, bmp, false, 0, true, out var d));
            Assert.Equal(entries[0].X, d.AnchorX);
            Assert.Equal(entries[0].Y, d.AnchorY);
        }
    }

    [Fact]
    public void Pixel_parity_sample_tree_670_on_blank_map()
    {
        var root = Lib();
        if (root is null) return;
        var built = AstriaGfxCatalogBuilder.Build(root);
        if (!built.Catalog.TryGet(GfxCategory.Object, 670, out var resource) || resource is null)
            return;

        const int w = 15, h = 17, cellId = 228;
        var cells = Enumerable.Range(0, MapGeometry.CellCount(w, h)).Select(_ => new CellData()).ToList();
        MapCellEditor.SetLayerGfx(cells[cellId], MapCellEditor.Layer.Object1, 670, false, 0);
        var map = new MapDocument { Id = 10439, Width = w, Height = h, Cells = cells };

        using var imgs = new CachedBitmapGfxProvider();
        if (!imgs.TryGetBitmap(resource, out var bmp)) return;
        var (ax, ay) = GfxPlacementMath.ResolveAnchor(resource.Anchor?.X, resource.Anchor?.Y, bmp.Width, bmp.Height);
        Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
            w, h, cellId, resource, bmp, false, 0, true, out var placement));

        var renderer = new AstriaMapRenderer(built.Catalog, imgs);
        var result = renderer.Render(map, new MapRenderOptions
        {
            DrawBackground = false,
            DrawGround = false,
            DrawObjectLayer2 = false,
        });
        using var finalImg = result.Image;

        using var previewCanvas = new Bitmap(finalImg.Width, finalImg.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var transformed = GfxPlacementMath.TransformBitmap(
                   bmp, ax, ay, false, 0, true, IsoGeometry.SizeBaseCell, out var logical))
        using (var g = Graphics.FromImage(previewCanvas))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(transformed, new Rectangle(
                placement.DrawXHit, placement.DrawYHit, logical.Width, logical.Height));
        }

        var x0 = Math.Max(0, placement.DrawXHit);
        var y0 = Math.Max(0, placement.DrawYHit);
        var x1 = Math.Min(finalImg.Width, placement.DrawXHit + placement.DrawWidth);
        var y1 = Math.Min(finalImg.Height, placement.DrawYHit + placement.DrawHeight);
        var compared = 0;
        var mismatch = 0;
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
        {
            var p = previewCanvas.GetPixel(x, y);
            if (p.A == 0) continue;
            var f = finalImg.GetPixel(x, y);
            compared++;
            var er = (p.R * p.A) / 255;
            var eg = (p.G * p.A) / 255;
            var eb = (p.B * p.A) / 255;
            if (Math.Abs(er - f.R) > 2 || Math.Abs(eg - f.G) > 2 || Math.Abs(eb - f.B) > 2)
                mismatch++;
        }

        Assert.True(compared > 1000);
        Assert.Equal(0, mismatch);
    }

    [Fact]
    public void Hit_space_applies_crop_once()
    {
        Assert.True(GfxPlacementPipeline.TryBuild(
            15, 17, 100,
            new GfxResource
            {
                Id = 1,
                Category = GfxCategory.Ground,
                FilePath = "x",
                Folder = "t",
                Extension = ".png",
                Anchor = new GfxAnchor(10, 10),
            },
            52, 26, false, 0, false,
            out var d));
        var crop = IsoGeometry.ExportCrop(15, 17);
        Assert.Equal(d.DrawXFull - crop.X, d.DrawXHit);
        Assert.Equal(d.DrawYFull - crop.Y, d.DrawYHit);
        Assert.Equal((26, 13, 728, 416), crop);
    }

    [Fact]
    public void Edge_cells_descriptor_stable()
    {
        var root = Lib();
        if (root is null) return;
        var built = AstriaGfxCatalogBuilder.Build(root);
        if (!built.Catalog.TryGet(GfxCategory.Object, 80, out var res) || res is null) return;
        using var imgs = new CachedBitmapGfxProvider();
        if (!imgs.TryGetBitmap(res, out var bmp)) return;

        foreach (var cellId in new[] { 0, 14, 465, 478 })
        {
            Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(
                15, 17, cellId, res, bmp, false, 0, true, out var preview));
            var corners = IsoGeometry.BuildCellCorners(15, 17);
            var (ax, ay) = GfxPlacementMath.ResolveAnchor(res.Anchor?.X, res.Anchor?.Y, bmp.Width, bmp.Height);
            var final = GfxPlacementMath.CalculateDrawPlacement(
                corners[cellId], bmp.Width, bmp.Height, ax, ay, false, 0, true);
            Assert.Equal(final.X, preview.DrawXFull);
            Assert.Equal(final.Y, preview.DrawYFull);
        }
    }
}
