using System.Diagnostics;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.Swf;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.AppSupport;

public sealed class Phase5EditSmokeTests
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
    public void Edit_10420_ground_object1_object2_then_clear_and_reload_semantics()
    {
        var root = ResolveAstriaRoot();
        if (root is null) return;

        using var cache = new CachedBitmapGfxProvider();
        var catalog = AstriaGfxCatalogBuilder.Build(root).Catalog;
        var renderer = new AstriaMapRenderer(catalog, cache);

        var map = Load(root, 10420);
        var originalMapData = map.MapData;
        var cellId = 154;
        var before = MapCellEditor.Clone(map.Cells[cellId]);

        var groundId = catalog.Enumerate(Domain.Gfx.GfxCategory.Ground).First().Id;
        var objectId = catalog.Enumerate(Domain.Gfx.GfxCategory.Object).First().Id;

        MapCellEditor.SetLayerGfx(map.Cells[cellId], MapCellEditor.Layer.Ground, groundId);
        MapCellEditor.SetLayerGfx(map.Cells[cellId], MapCellEditor.Layer.Object1, objectId);
        MapCellEditor.SetLayerGfx(map.Cells[cellId], MapCellEditor.Layer.Object2, objectId);
        MapCellEditor.SetMovement(map.Cells[cellId], MovementType.Path);
        MapCellEditor.SyncMapDataString(map);

        var sw = Stopwatch.StartNew();
        var editedRender = renderer.Render(map);
        sw.Stop();
        Assert.True(editedRender.Image.Width > 0);
        editedRender.Image.Dispose();
        Console.WriteLine($"Phase5 edit→render latency: {sw.ElapsedMilliseconds} ms");

        var decoded = MapDataCodec.DecodeMap(map.MapData)[cellId];
        Assert.Equal(groundId, decoded.GroundGfxId);
        Assert.Equal(objectId, decoded.Object1GfxId);
        Assert.Equal(objectId, decoded.Object2GfxId);
        Assert.Equal(MovementType.Path, decoded.Movement);

        MapCellEditor.ClearLayer(map.Cells[cellId], MapCellEditor.Layer.Ground);
        MapCellEditor.ClearLayer(map.Cells[cellId], MapCellEditor.Layer.Object1);
        MapCellEditor.ClearLayer(map.Cells[cellId], MapCellEditor.Layer.Object2);
        MapCellEditor.SyncMapDataString(map);
        var cleared = MapDataCodec.DecodeMap(map.MapData)[cellId];
        Assert.Equal(0, cleared.GroundGfxId);
        Assert.Equal(0, cleared.Object1GfxId);
        Assert.Equal(0, cleared.Object2GfxId);
        Assert.Equal(MovementType.Path, cleared.Movement); // clear layer must not wipe movement

        // Reload original (same as UI Recargar)
        var reloaded = Load(root, 10420);
        Assert.Equal(originalMapData, reloaded.MapData);
        Assert.True(MapCellEditor.CellEquals(before, reloaded.Cells[cellId]));
    }

    [Fact]
    public void Catalog_folders_come_from_disk_not_hardcoded()
    {
        var root = ResolveAstriaRoot();
        if (root is null) return;

        var catalog = AstriaGfxCatalogBuilder.Build(root).Catalog;
        var folders = catalog.Enumerate(Domain.Gfx.GfxCategory.Object)
            .Select(r => r.Folder)
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f)
            .ToList();

        Assert.Contains(folders, f => f.Equals("Arbres", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(folders, f => f.Equals("Eau", StringComparison.OrdinalIgnoreCase));
        Assert.True(folders.Count >= 10);
        Console.WriteLine("Object folders: " + string.Join(", ", folders));
    }

    private static MapDocument Load(string root, int mapId)
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
