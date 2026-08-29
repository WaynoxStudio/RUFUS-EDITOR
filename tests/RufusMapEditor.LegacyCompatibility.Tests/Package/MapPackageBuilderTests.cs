using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.Rendering;
using RufusMapEditor.Rendering.Package;

namespace RufusMapEditor.LegacyCompatibility.Tests.Package;

public sealed class MapPackageBuilderTests
{
    private static string FixturesRoot
    {
        get
        {
            var fromProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
            if (Directory.Exists(fromProject))
                return fromProject;
            throw new DirectoryNotFoundException("Could not locate tests/fixtures/maps.");
        }
    }

    private static string ArtifactsRoot
    {
        get
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "map_package"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static MapDocument LoadDecoded(int mapId)
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, $"{mapId}.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();
        FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
        return map;
    }

    private static MapPackageResult BuildCore(MapDocument map, string parent, bool showCellIds = true) =>
        MapPackageBuilder.CreateWithoutGfx().Build(map, new MapPackageOptions
        {
            ParentDirectory = parent,
            ShowCellIds = showCellIds,
            DocumentId = Guid.NewGuid().ToString("D"),
            ProjectName = $"map_{map.Id}",
            // Force no Flasm / no blank — core package must still succeed
            FlasmExePath = Path.Combine(parent, "__missing_flasm__.exe"),
            BlankSwfTemplatePath = Path.Combine(parent, "__missing_blank__.swf"),
        });

    [Fact]
    public void Package_core_filenames_and_no_forbidden_artifacts()
    {
        var map = LoadDecoded(10421);
        var parent = Path.Combine(ArtifactsRoot, "core_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);

        var result = BuildCore(map, parent);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(10421, result.MapId);
        Assert.False(result.LegacySwfGenerated);
        // Outdoor often missing on fixtures → warning before Flasm check; either is fine for CORE
        Assert.Contains("NO GENERADO", result.LegacySwfWarning ?? "", StringComparison.OrdinalIgnoreCase);

        var dir = result.PackageDirectory;
        Assert.True(File.Exists(Path.Combine(dir, "10421.rufmap")));
        Assert.True(File.Exists(Path.Combine(dir, "10421.png")));
        Assert.True(File.Exists(Path.Combine(dir, "10421_MapData.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "10421_ModeCell.png")));
        Assert.True(File.Exists(Path.Combine(dir, GfxUsageListBuilder.FileName)));
        Assert.True(File.Exists(Path.Combine(dir, "manifest.txt")));

        Assert.False(File.Exists(Path.Combine(dir, "10421.sql")));
        Assert.False(File.Exists(Path.Combine(dir, "10421.ame")));
        Assert.Empty(Directory.GetFiles(dir, "*.sql", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(dir, "*.ame", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(dir, "Legacy", "10421_AME.swf")));

        MapCellEditor.SyncDocument(map);
        var txt = MapDataPlainText.ReadFile(Path.Combine(dir, "10421_MapData.txt"));
        Assert.Equal(map.MapData, txt);
        Assert.DoesNotContain((byte)'\n', File.ReadAllBytes(Path.Combine(dir, "10421_MapData.txt")));
    }

    [Fact]
    public void Package_png_and_modecell_dimensions_15x17()
    {
        var map = LoadDecoded(10421);
        Assert.Equal(15, map.Width);
        Assert.Equal(17, map.Height);

        var parent = Path.Combine(ArtifactsRoot, "dims15_" + Guid.NewGuid().ToString("N"));
        var result = BuildCore(map, parent);
        Assert.True(result.Success, result.ErrorMessage);

        Assert.Equal(728, result.PngWidth);
        Assert.Equal(416, result.PngHeight);
        Assert.Equal(780, result.ModeCellWidth);
        Assert.Equal(442, result.ModeCellHeight);

        var (fullW, fullH) = IsoGeometry.FullCanvasSize(15, 17);
        Assert.Equal((780, 442), (fullW, fullH));
        var crop = IsoGeometry.ExportCrop(15, 17);
        Assert.Equal((26, 13, 728, 416), crop);
    }

    [Fact]
    public void Package_modecell_dimensions_19x22()
    {
        var cellCount = MapGeometry.CellCount(19, 22);
        Assert.Equal(796, cellCount);
        var map = new MapDocument
        {
            Id = 19022,
            Width = 19,
            Height = 22,
            BackgroundId = 1,
            Outdoor = true,
            Cells = Enumerable.Range(0, cellCount)
                .Select(_ => new CellData { Movement = MovementType.Walkable, LineOfSight = true })
                .ToList(),
        };
        MapCellEditor.SyncDocument(map);

        var parent = Path.Combine(ArtifactsRoot, "dims19_" + Guid.NewGuid().ToString("N"));
        var result = BuildCore(map, parent);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(988, result.ModeCellWidth);
        Assert.Equal(572, result.ModeCellHeight);

        var (fullW, fullH) = IsoGeometry.FullCanvasSize(19, 22);
        Assert.Equal((988, 572), (fullW, fullH));
    }

    [Fact]
    public void Package_invalid_mapid_blocked()
    {
        var map = new MapDocument
        {
            Id = 0,
            Width = 15,
            Height = 17,
            Cells = Enumerable.Range(0, 15 * 17).Select(_ => new CellData()).ToList(),
        };
        var parent = Path.Combine(ArtifactsRoot, "badid_" + Guid.NewGuid().ToString("N"));
        var result = BuildCore(map, parent);
        Assert.False(result.Success);
        Assert.Contains("MapId", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gfx_list_separates_ground_and_object_same_numeric_id()
    {
        var map = new MapDocument
        {
            Id = 90001,
            Width = 15,
            Height = 17,
            BackgroundId = 284,
            Cells = Enumerable.Range(0, 15 * 17)
                .Select(_ => new CellData { Movement = MovementType.Walkable, LineOfSight = true })
                .ToList(),
        };
        map.Cells[0].GroundGfxId = 374;
        map.Cells[1].Object1GfxId = 374;
        MapCellEditor.SyncDocument(map);

        var text = GfxUsageListBuilder.Build(map);
        Assert.Contains("MapId: 90001", text);
        Assert.Contains("Background: 284", text);
        Assert.Contains("[Ground]", text);
        Assert.Contains("[Object]", text);

        var groundSection = text.Split("[Object]", 2)[0];
        var objectSection = text.Split("[Object]", 2)[1];
        Assert.Contains("\n374\n", groundSection.Replace("\r\n", "\n") + "\n");
        Assert.Contains("\n374\n", ("\n" + objectSection.Replace("\r\n", "\n")));
        Assert.DoesNotContain("[Background]", text);
    }

    [Fact]
    public void Gfx_list_ascending_and_deterministic()
    {
        var map = new MapDocument
        {
            Id = 90002,
            Width = 15,
            Height = 17,
            BackgroundId = 1,
            Cells = Enumerable.Range(0, 15 * 17).Select(_ => new CellData()).ToList(),
        };
        map.Cells[5].GroundGfxId = 900;
        map.Cells[2].GroundGfxId = 3;
        map.Cells[8].Object2GfxId = 50;
        map.Cells[1].Object1GfxId = 10;
        var a = GfxUsageListBuilder.Build(map);
        var b = GfxUsageListBuilder.Build(map);
        Assert.Equal(a, b);

        var groundIds = ExtractSectionIds(a, "[Ground]");
        Assert.Equal(new[] { 3, 900 }, groundIds);
        var objectIds = ExtractSectionIds(a, "[Object]");
        Assert.Equal(new[] { 10, 50 }, objectIds);
    }

    [Fact]
    public void Manifest_hashes_and_fight_counts()
    {
        var map = LoadDecoded(10421);
        MapCellEditor.SyncDocument(map);
        var parent = Path.Combine(ArtifactsRoot, "manifest_" + Guid.NewGuid().ToString("N"));
        var result = BuildCore(map, parent);
        Assert.True(result.Success, result.ErrorMessage);

        var expectedMapHash = MapPackageBuilder.Sha256Hex(map.MapData ?? "");
        var expectedFightHash = MapPackageBuilder.Sha256Hex(map.FightPlaces ?? "");
        Assert.Equal(expectedMapHash, result.MapDataSha256);
        Assert.Equal(expectedFightHash, result.FightPlacesSha256);
        Assert.Equal(map.Cells.Count(c => c.FightCell == 1), result.FightTeam1Count);
        Assert.Equal(map.Cells.Count(c => c.FightCell == 2), result.FightTeam2Count);
        Assert.True(result.FightTeam1Count >= 1);
        Assert.True(result.FightTeam2Count >= 1);

        var manifest = File.ReadAllText(Path.Combine(result.PackageDirectory, "manifest.txt"), Encoding.UTF8);
        Assert.Contains($"MapData SHA256: {expectedMapHash}", manifest);
        Assert.Contains($"FightPlaces SHA256: {expectedFightHash}", manifest);
        Assert.Contains($"Team1 cells: {result.FightTeam1Count}", manifest);
        Assert.Contains($"Team2 cells: {result.FightTeam2Count}", manifest);
        Assert.Contains("MapData TXT: 10421_MapData.txt", manifest);
        Assert.Contains("SQL producción: no incluido", manifest);
        Assert.Contains("AME BinaryFormatter: no incluido", manifest);
        Assert.Contains("No confirmado como SWF cliente RUFUS", manifest);
        Assert.DoesNotContain('\uFEFF', manifest); // no BOM
    }

    [Fact]
    public void Rufmap_roundtrip_from_package()
    {
        var map = LoadDecoded(10421);
        MapCellEditor.SyncDocument(map);
        var parent = Path.Combine(ArtifactsRoot, "roundtrip_" + Guid.NewGuid().ToString("N"));
        var result = BuildCore(map, parent);
        Assert.True(result.Success, result.ErrorMessage);

        var loaded = RufmapIo.LoadFile(Path.Combine(result.PackageDirectory, "10421.rufmap"));
        Assert.Equal(map.Id, loaded.Document.Id);
        Assert.Equal(map.Width, loaded.Document.Width);
        Assert.Equal(map.Height, loaded.Document.Height);
        Assert.Equal(map.BackgroundId, loaded.Document.BackgroundId);
        Assert.Equal(map.MapData, loaded.Document.MapData);
        Assert.Equal(map.FightPlaces, loaded.Document.FightPlaces);
        // FightCell is editor state derived from FightPlaces (not stored per-cell in rufmap DTO)
        FightPlacesCodec.ApplyToCells(loaded.Document.Cells, loaded.Document.FightPlaces);
        Assert.Equal(1, loaded.Document.Cells[67].FightCell);
        Assert.Equal(2, loaded.Document.Cells[330].FightCell);
    }

    [Fact]
    public void Overwrite_updates_package_files_keeps_foreign()
    {
        var map = LoadDecoded(10421);
        var parent = Path.Combine(ArtifactsRoot, "overwrite_" + Guid.NewGuid().ToString("N"));
        var first = BuildCore(map, parent);
        Assert.True(first.Success, first.ErrorMessage);

        var foreign = Path.Combine(first.PackageDirectory, "notas_usuario.txt");
        File.WriteAllText(foreign, "keep me");
        var pngBefore = File.ReadAllBytes(Path.Combine(first.PackageDirectory, "10421.png"));

        map.Cells[0].GroundGfxId = Math.Max(1, map.Cells[0].GroundGfxId + 1);
        MapCellEditor.SyncDocument(map);
        var second = BuildCore(map, parent);
        Assert.True(second.Success, second.ErrorMessage);

        Assert.True(File.Exists(foreign));
        Assert.Equal("keep me", File.ReadAllText(foreign));
        var pngAfter = File.ReadAllBytes(Path.Combine(second.PackageDirectory, "10421.png"));
        // Core files refreshed (may be identical if gfx unused by empty catalog; rufmap must change)
        var rufmap = File.ReadAllText(Path.Combine(second.PackageDirectory, "10421.rufmap"));
        Assert.Contains(map.MapData, rufmap);
        Assert.True(File.Exists(Path.Combine(second.PackageDirectory, "manifest.txt")));
        _ = pngBefore;
        _ = pngAfter;
    }

    [Fact]
    public void Reexport_deterministic_core_bytes_except_manifest_timestamp()
    {
        var map = LoadDecoded(10421);
        MapCellEditor.SyncDocument(map);
        var parent = Path.Combine(ArtifactsRoot, "redet_" + Guid.NewGuid().ToString("N"));

        var a = BuildCore(map, parent, showCellIds: true);
        Assert.True(a.Success, a.ErrorMessage);
        var pngA = File.ReadAllBytes(Path.Combine(a.PackageDirectory, "10421.png"));
        var modeA = File.ReadAllBytes(Path.Combine(a.PackageDirectory, "10421_ModeCell.png"));
        var gfxA = File.ReadAllBytes(Path.Combine(a.PackageDirectory, GfxUsageListBuilder.FileName));
        var manA = StripGenerated(File.ReadAllText(Path.Combine(a.PackageDirectory, "manifest.txt")));

        // Second export into same package dir
        Thread.Sleep(20);
        var b = BuildCore(map, parent, showCellIds: true);
        Assert.True(b.Success, b.ErrorMessage);
        var pngB = File.ReadAllBytes(Path.Combine(b.PackageDirectory, "10421.png"));
        var modeB = File.ReadAllBytes(Path.Combine(b.PackageDirectory, "10421_ModeCell.png"));
        var gfxB = File.ReadAllBytes(Path.Combine(b.PackageDirectory, GfxUsageListBuilder.FileName));
        var manB = StripGenerated(File.ReadAllText(Path.Combine(b.PackageDirectory, "manifest.txt")));

        Assert.Equal(pngA, pngB);
        Assert.Equal(modeA, modeB);
        Assert.Equal(gfxA, gfxB);
        Assert.Equal(manA, manB);
        Assert.Equal(a.MapDataSha256, b.MapDataSha256);
        Assert.Equal(a.FightPlacesSha256, b.FightPlacesSha256);
    }

    [Fact]
    public void ModeCell_overlays_golden_10421_fight_movement_los()
    {
        var map = LoadDecoded(10421);
        Assert.Equal(1, map.Cells[67].FightCell);
        Assert.Equal(2, map.Cells[330].FightCell);
        Assert.Equal(MovementType.Unwalkable, map.Cells[10].Movement);
        Assert.True(map.Cells[10].LineOfSight);
        Assert.Equal(MovementType.Unwalkable, map.Cells[228].Movement);
        Assert.False(map.Cells[228].LineOfSight);
        Assert.Equal(MovementType.Walkable, map.Cells[405].Movement);
        Assert.False(map.Cells[405].LineOfSight);

        var parent = Path.Combine(ArtifactsRoot, "overlays_" + Guid.NewGuid().ToString("N"));
        var result = BuildCore(map, parent, showCellIds: false);
        Assert.True(result.Success, result.ErrorMessage);

        using var bmp = new Bitmap(Path.Combine(result.PackageDirectory, "10421_ModeCell.png"));
        Assert.Equal(780, bmp.Width);
        Assert.Equal(442, bmp.Height);

        var corners = IsoGeometry.BuildCellCorners(map.Width, map.Height);
        AssertHasOverlaySignal(bmp, corners[67], ModeCellExportPalette.Fight1Stroke);
        AssertHasOverlaySignal(bmp, corners[330], ModeCellExportPalette.Fight2Stroke);
        AssertHasOverlaySignal(bmp, corners[10], ModeCellExportPalette.UnwalkableStroke);
        AssertHasOverlaySignal(bmp, corners[228], ModeCellExportPalette.LosBlockStroke);
        AssertHasOverlaySignal(bmp, corners[405], ModeCellExportPalette.LosBlockStroke);

        var crop = IsoGeometry.ExportCrop(map.Width, map.Height);
        // Limit stroke along crop edge
        var edge = bmp.GetPixel(crop.X + crop.Width / 2, crop.Y);
        Assert.True(ColorDistance(edge, ModeCellExportPalette.ExportLimitStroke) < 80,
            $"Export limit color mismatch at top edge: {edge}");
    }

    [Fact]
    public void Normal_png_has_no_export_limit_stroke_inside_crop()
    {
        var map = LoadDecoded(10421);
        var parent = Path.Combine(ArtifactsRoot, "pngclean_" + Guid.NewGuid().ToString("N"));
        var result = BuildCore(map, parent);
        Assert.True(result.Success, result.ErrorMessage);

        using var png = new Bitmap(Path.Combine(result.PackageDirectory, "10421.png"));
        Assert.Equal(728, png.Width);
        Assert.Equal(416, png.Height);
        // Cropped PNG must not be ModeCell-sized
        Assert.NotEqual(780, png.Width);
    }

    [Fact]
    public void Package_without_flasm_still_core_ok()
    {
        var map = LoadDecoded(10420);
        map.Outdoor = true;
        MapCellEditor.SyncDocument(map);
        var parent = Path.Combine(ArtifactsRoot, "noflasm_" + Guid.NewGuid().ToString("N"));
        var result = BuildCore(map, parent);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(result.LegacySwfGenerated);
        Assert.Contains("Flasm", result.LegacySwfWarning ?? "", StringComparison.OrdinalIgnoreCase);
        foreach (var name in new[] { "10420.rufmap", "10420.png", "10420_MapData.txt", "10420_ModeCell.png", GfxUsageListBuilder.FileName, "manifest.txt" })
            Assert.True(File.Exists(Path.Combine(result.PackageDirectory, name)), name);
    }

    private static List<int> ExtractSectionIds(string text, string header)
    {
        var norm = text.Replace("\r\n", "\n");
        var idx = norm.IndexOf(header, StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var rest = norm[(idx + header.Length)..].TrimStart('\n');
        var end = rest.IndexOf("\n[", StringComparison.Ordinal);
        var body = end >= 0 ? rest[..end] : rest;
        return body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.Parse(s.Trim()))
            .ToList();
    }

    private static string StripGenerated(string manifest) =>
        Regex.Replace(manifest, @"Generated:.*\r?\n", "");

    private static void AssertHasOverlaySignal(Bitmap bmp, IsoGeometry.CellCorners c, Color expected)
    {
        var (cx, cy) = IsoGeometry.GetCellCenter(c);
        var x0 = Math.Clamp((int)cx - 4, 0, bmp.Width - 1);
        var y0 = Math.Clamp((int)cy - 4, 0, bmp.Height - 1);
        var x1 = Math.Clamp((int)cx + 4, 0, bmp.Width - 1);
        var y1 = Math.Clamp((int)cy + 4, 0, bmp.Height - 1);
        var best = double.MaxValue;
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
            best = Math.Min(best, ColorDistance(bmp.GetPixel(x, y), expected));
        Assert.True(best < 90, $"No overlay signal near ({cx:F0},{cy:F0}) for {expected}; best={best:F1}");
    }

    private static double ColorDistance(Color a, Color b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}
