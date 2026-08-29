using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.Swf;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.AppSupport;

/// <summary>
/// Non-UI smoke covering the same load/render/hit-test path used by the WPF viewer.
/// </summary>
public sealed class Phase4ViewerPipelineTests
{
    private const string DefaultAstriaRoot = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1";

    private static string? ResolveAstriaRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ASTRIA_MAP_EDITOR_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;
        return Directory.Exists(DefaultAstriaRoot) ? DefaultAstriaRoot : null;
    }

    [Fact]
    public void Discover_maps_via_sql_folders()
    {
        var root = ResolveAstriaRoot();
        if (root is null) return;

        var mapsDir = Path.Combine(root, "Maps");
        var ids = Directory.EnumerateDirectories(mapsDir)
            .Select(Path.GetFileName)
            .Where(n => int.TryParse(n, out _))
            .Where(n => File.Exists(Path.Combine(mapsDir, n!, $"{n}.sql")))
            .Select(n => int.Parse(n!))
            .OrderBy(x => x)
            .ToList();

        Assert.Contains(10420, ids);
        Assert.True(ids.Count >= 30);
    }

    [Fact]
    public void Load_render_hit_10420_and_resource_gap_30001()
    {
        var root = ResolveAstriaRoot();
        if (root is null) return;

        using var cache = new CachedBitmapGfxProvider();
        var catalog = AstriaGfxCatalogBuilder.Build(root).Catalog;
        var renderer = new AstriaMapRenderer(catalog, cache);

        var map10420 = Load(root, 10420);
        var r1 = renderer.Render(map10420);
        Assert.Equal(728, r1.Image.Width);
        Assert.Equal(416, r1.Image.Height);
        Assert.Empty(r1.MissingGfx);
        var hit = new IsoHitTester(map10420.Width, map10420.Height);
        Assert.True(hit.TryGetCellCornersInHitSpace(154, out var c154));
        var cx = (c154.A.X + c154.C.X) / 2.0;
        var cy = (c154.B.Y + c154.D.Y) / 2.0;
        Assert.Equal(154, hit.HitTest(cx, cy));
        r1.Image.Dispose();

        var map30001 = Load(root, 30001);
        var r2 = renderer.Render(map30001);
        Assert.Contains(r2.MissingGfx, g => g.Contains("Background:340"));
        Assert.True(r2.Image.Width > 0);
        r2.Image.Dispose();
    }

    private static Domain.Maps.MapDocument Load(string root, int mapId)
    {
        var sql = Path.Combine(root, "Maps", mapId.ToString(), $"{mapId}.sql");
        var map = AstriaSqlMapParser.ParseFile(sql);
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        var swf = FlasmSwfMetadataReader.ResolvePreferredSwf(Path.Combine(root, "Maps", mapId.ToString()), mapId);
        if (swf is not null)
        {
            var meta = FlasmSwfMetadataReader.Read(swf, Path.Combine(root, "Flasm", "flasm.exe"));
            FlasmSwfMetadataReader.ApplyToDocument(map, meta);
        }
        return map;
    }
}
