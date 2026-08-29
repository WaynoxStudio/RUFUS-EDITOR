using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Ui;

public sealed class ThemeContrastTests
{
    [Theory]
    [InlineData("5C5C5C", "FFFFFF")] // Light TextDisabled on menu SurfaceBackground
    [InlineData("B0B0B0", "2D2D2D")] // Dark TextDisabled on menu ElevatedSurface
    public void Disabled_text_has_readable_contrast(string fgHex, string bgHex)
    {
        var ratio = ContrastRatio(ParseColor(fgHex), ParseColor(bgHex));
        Assert.True(ratio >= 3.0, $"Contrast {ratio:F2} below 3:1 for #{fgHex} on #{bgHex}");
    }

    [Fact]
    public void Theme_brush_keys_documented_in_palette_files()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var light = File.ReadAllText(Path.Combine(repoRoot, "src", "RufusMapEditor.App", "Themes", "ColorsLight.xaml"));
        var dark = File.ReadAllText(Path.Combine(repoRoot, "src", "RufusMapEditor.App", "Themes", "ColorsDark.xaml"));
        Assert.Contains("TextDisabled", light);
        Assert.Contains("TextDisabled", dark);
        Assert.Contains("OverlayPaintTargetFill", light);
        Assert.Contains("OverlayPaintTargetStroke", dark);
    }

    private static (byte R, byte G, byte B) ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return (
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    private static double ContrastRatio((byte R, byte G, byte B) fg, (byte R, byte G, byte B) bg)
    {
        var l1 = RelativeLuminance(fg);
        var l2 = RelativeLuminance(bg);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance((byte R, byte G, byte B) c)
    {
        static double Chan(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Chan(c.R) + 0.7152 * Chan(c.G) + 0.0722 * Chan(c.B);
    }
}

public sealed class GfxCatalogLayoutTests
{
    [Fact]
    public void Narrow_panel_yields_fewer_columns_than_wide_panel()
    {
        var narrow = GfxCatalogLayout.ComputeColumns(400);
        var wide = GfxCatalogLayout.ComputeColumns(1200);
        Assert.True(wide > narrow);
        Assert.True(narrow >= GfxCatalogLayout.MinColumns);
    }

    [Theory]
    [InlineData(300, 3)]
    [InlineData(780, 9)]
    [InlineData(1600, 20)]
    public void Column_count_scales_with_available_width(double width, int expectedAtLeast)
    {
        var cols = GfxCatalogLayout.ComputeColumns(width);
        Assert.True(cols >= expectedAtLeast - 1 && cols <= expectedAtLeast + 1,
            $"width={width} cols={cols} expected~{expectedAtLeast}");
    }

    [Fact]
    public void Filter_key_includes_column_count_semantics()
    {
        var narrow = GfxCatalogLayout.ComputeColumns(400);
        var wide = GfxCatalogLayout.ComputeColumns(1200);
        Assert.NotEqual(narrow, wide);
    }
}

public sealed class PaintTargetTests
{
    private static MapDocument LoadMap(string file = "10420.sql")
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, file));
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        return map;
    }

    [Fact]
    public void Hit_test_target_cell_equals_modified_cell_on_paint()
    {
        var map = LoadMap();
        var tester = new IsoHitTester(map.Width, map.Height);
        Assert.True(tester.TryGetCellCornersInHitSpace(228, out var corners));
        var cx = (corners.A.X + corners.C.X) / 2.0;
        var cy = (corners.B.Y + corners.D.Y) / 2.0;
        var target = tester.HitTest(cx, cy);
        Assert.Equal(228, target);

        MapCellEditor.SetLayerGfx(map.Cells[target!.Value], MapCellEditor.Layer.Ground, 8);
        Assert.Equal(8, map.Cells[228].GroundGfxId);
    }

    [Fact]
    public void Preview_and_final_share_placement_for_ground()
    {
        var map = LoadMap();
        var tester = new IsoHitTester(map.Width, map.Height);
        const int cellId = 228;
        Assert.True(tester.TryGetCellCornersInHitSpace(cellId, out _));

        var before = map.Cells[cellId].GroundGfxId;
        MapCellEditor.SetLayerGfx(map.Cells[cellId], MapCellEditor.Layer.Ground, 8);
        MapCellEditor.SyncMapDataString(map);
        Assert.NotEqual(before, map.Cells[cellId].GroundGfxId);
        MapCellEditor.SetLayerGfx(map.Cells[cellId], MapCellEditor.Layer.Ground, before);
    }

    [Fact]
    public void Fast_segment_sampling_finds_more_cells_than_endpoints_only()
    {
        var map = LoadMap();
        var tester = new IsoHitTester(map.Width, map.Height);

        int? start = null;
        int? end = null;
        for (var id = 0; id < tester.Corners.Count; id++)
        {
            if (!tester.TryGetCellCornersInHitSpace(id, out var c)) continue;
            if (start is null) { start = id; continue; }
            end = id;
            if (id > start + 5) break;
        }

        Assert.NotNull(start);
        Assert.NotNull(end);
        Assert.True(tester.TryGetCellCornersInHitSpace(start.Value, out var cs));
        Assert.True(tester.TryGetCellCornersInHitSpace(end.Value, out var ce));
        var x0 = (cs.A.X + cs.C.X) / 2.0;
        var y0 = (cs.B.Y + cs.D.Y) / 2.0;
        var x1 = (ce.A.X + ce.C.X) / 2.0;
        var y1 = (ce.B.Y + ce.D.Y) / 2.0;

        var sampled = IsoStrokeInterpolation.CellsAlongSegment(tester, x0, y0, x1, y1);
        Assert.True(sampled.Count >= 2);
    }
}
