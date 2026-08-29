using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.Swf;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Rendering;

public sealed class MapRenderGoldenTests
{
    private const string DefaultAstriaRoot = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1";

    private static string? ResolveAstriaRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ASTRIA_MAP_EDITOR_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;
        return Directory.Exists(DefaultAstriaRoot) ? DefaultAstriaRoot : null;
    }

    private static string ArtifactsDir
    {
        get
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "render"));
            Directory.CreateDirectory(path);
            return path;
        }
    }

    private static string LogoPath
    {
        get
        {
            var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "astria_logo_map.png"));
            if (File.Exists(fixture)) return fixture;
            var fromRef = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_refs", "AstriaMapEditor", "AstriaMapEditor", "Resources", "logo_map.png"));
            return fromRef;
        }
    }

    [Fact]
    public void Export_dimensions_for_15x17_are_728x416()
    {
        var (w, h) = IsoGeometry.ExportImageSize(15, 17);
        Assert.Equal(728, w);
        Assert.Equal(416, h);
        var (fw, fh) = IsoGeometry.FullCanvasSize(15, 17);
        Assert.Equal(780, fw);
        Assert.Equal(442, fh);
    }

    [Fact]
    public void Map_10420_render_matches_astria_golden_png()
    {
        var root = ResolveAstriaRoot();
        if (root is null) return;

        using var cache = new CachedBitmapGfxProvider();
        var catalog = AstriaGfxCatalogBuilder.Build(root).Catalog;
        var map = LoadMap(root, 10420);
        var renderer = new AstriaMapRenderer(catalog, cache);
        var result = renderer.Render(map, new MapRenderOptions { AstriaLogoPath = LogoPath });

        var outPath = Path.Combine(ArtifactsDir, "render_10420.png");
        result.Image.Save(outPath, ImageFormat.Png);

        var goldenPath = Path.Combine(root, "Maps", "10420", "10420.png");
        using var golden = new Bitmap(goldenPath);
        var cmp = ImageComparer.Compare(result.Image, golden);

        Console.WriteLine(
            $"10420: size {cmp.WidthB}x{cmp.HeightB} vs {cmp.WidthA}x{cmp.HeightA}; " +
            $"diffPixels={cmp.DifferentPixels} ({cmp.DifferentPercent:F4}%); mean={cmp.MeanAbsDifference:F3}; max={cmp.MaxChannelDifference}; " +
            $"bbox={cmp.DiffBoundingBox}; missingGfx={result.MissingGfx.Count}; missingAnchors={result.MissingAnchors.Count}; " +
            $"renderMs={result.Metrics.Render.TotalMilliseconds:F1}; draws={result.Metrics.DrawOperations}; uniqueImg={result.Metrics.UniqueImagesLoaded}");

        if (!cmp.Identical)
        {
            var diffPath = Path.Combine(ArtifactsDir, "diff_10420.png");
            using var diff = ImageComparer.CreateDiffImage(result.Image, golden);
            diff.Save(diffPath, ImageFormat.Png);
        }

        Assert.True(result.MissingGfx.Count == 0, "Missing GFX: " + string.Join(", ", result.MissingGfx));
        Assert.True(result.MissingAnchors.Count == 0, "Missing anchors: " + string.Join(", ", result.MissingAnchors));
        Assert.True(cmp.SameDimensions, $"Dimension mismatch: RUFUS {cmp.WidthA}x{cmp.HeightA} vs Astria {cmp.WidthB}x{cmp.HeightB}");

        // Astria golden PNGs were produced with .NET Framework 4.x GDI+.
        // System.Drawing.Common on .NET 10 yields tiny bilinear/alpha variances (typically max≤7, mean≪1).
        // Structural bugs produce much larger mean/max — see docs/ASTRIA_COMPATIBILITY.md.
        Assert.True(cmp.MaxChannelDifference <= 8,
            $"Suspected structural render bug: max channel diff {cmp.MaxChannelDifference} > 8. mean={cmp.MeanAbsDifference:F3}, diff%={cmp.DifferentPercent:F3}. See {ArtifactsDir}");
        Assert.True(cmp.MeanAbsDifference < 0.5,
            $"Suspected structural render bug: mean abs diff {cmp.MeanAbsDifference:F3} >= 0.5. See {ArtifactsDir}");
    }

    [Fact]
    public void All_fixture_maps_render_report()
    {
        var root = ResolveAstriaRoot();
        if (root is null) return;

        using var cache = new CachedBitmapGfxProvider();
        var catalog = AstriaGfxCatalogBuilder.Build(root).Catalog;
        var renderer = new AstriaMapRenderer(catalog, cache);
        var reportPath = Path.Combine(ArtifactsDir, "render_report.md");
        var sb = new StringBuilder();
        sb.AppendLine("| MapID | Tamaño Astria | Tamaño RUFUS | Missing GFX | Missing Anchors | Pixel Diff | Diff % | Estado |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        var nearIdentical = 0;
        var different = 0;
        var resourceGaps = 0;
        var crashes = 0;

        var mapDirs = Directory.GetDirectories(Path.Combine(root, "Maps"))
            .Where(d => !Path.GetFileName(d).Equals("AutoSave", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);

        foreach (var dir in mapDirs)
        {
            if (!int.TryParse(Path.GetFileName(dir), out var mapId))
                continue;

            try
            {
                var map = LoadMap(root, mapId);
                var result = renderer.Render(map, new MapRenderOptions { AstriaLogoPath = LogoPath });
                var renderPath = Path.Combine(ArtifactsDir, $"render_{mapId}.png");
                result.Image.Save(renderPath, ImageFormat.Png);

                var goldenPath = Path.Combine(dir, $"{mapId}.png");
                using var golden = new Bitmap(goldenPath);
                var cmp = ImageComparer.Compare(result.Image, golden);
                result.Image.Dispose();

                var status = cmp.Identical ? "IDENTICAL" :
                    result.MissingGfx.Count > 0 || result.MissingAnchors.Count > 0 ? "RESOURCE_GAP" :
                    (cmp.MeanAbsDifference < 0.5 && cmp.MaxChannelDifference <= 8) ? "NEAR_IDENTICAL" :
                    "DIFFERENT";

                if (status is "IDENTICAL" or "NEAR_IDENTICAL") nearIdentical++;
                else if (status == "RESOURCE_GAP") resourceGaps++;
                else different++;

                if (!cmp.Identical && cmp.SameDimensions)
                {
                    using var rendered = new Bitmap(renderPath);
                    using var g2 = new Bitmap(goldenPath);
                    using var diff = ImageComparer.CreateDiffImage(rendered, g2);
                    diff.Save(Path.Combine(ArtifactsDir, $"diff_{mapId}.png"), ImageFormat.Png);
                }

                sb.AppendLine(
                    $"| {mapId} | {cmp.WidthB}x{cmp.HeightB} | {cmp.WidthA}x{cmp.HeightA} | {result.MissingGfx.Count} | {result.MissingAnchors.Count} | {cmp.DifferentPixels} | {cmp.DifferentPercent:F4} | {status} |");
            }
            catch (Exception ex)
            {
                crashes++;
                sb.AppendLine($"| {mapId} | - | - | - | - | - | - | ERROR: {ex.Message.Replace("|", "/")} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Summary: near_identical={nearIdentical}, different={different}, resource_gaps={resourceGaps}, crashes={crashes}");
        File.WriteAllText(reportPath, sb.ToString());
        Console.WriteLine(sb.ToString());

        Assert.True(nearIdentical + different + resourceGaps + crashes > 0);
        Assert.Equal(0, crashes);
    }

    private static MapDocument LoadMap(string astriaRoot, int mapId)
    {
        var sqlPath = Path.Combine(astriaRoot, "Maps", mapId.ToString(), $"{mapId}.sql");
        // Prefer fixture copy when present
        var fixtureSql = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps", $"{mapId}.sql"));
        if (File.Exists(fixtureSql))
            sqlPath = fixtureSql;

        var map = AstriaSqlMapParser.ParseFile(sqlPath);
        map.Cells = RufusMapEditor.LegacyCompatibility.MapData.MapDataCodec.DecodeMap(map.MapData);

        var mapFolder = Path.Combine(astriaRoot, "Maps", mapId.ToString());
        var swf = FlasmSwfMetadataReader.ResolvePreferredSwf(mapFolder, mapId);
        if (swf is not null)
        {
            var flasm = Path.Combine(astriaRoot, "Flasm", "flasm.exe");
            var meta = FlasmSwfMetadataReader.Read(swf, flasm);
            FlasmSwfMetadataReader.ApplyToDocument(map, meta);
        }

        return map;
    }
}
