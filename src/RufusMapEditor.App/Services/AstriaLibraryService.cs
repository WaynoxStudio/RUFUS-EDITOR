using System.IO;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.Swf;
using RufusMapEditor.Rendering;
using RufusMapEditor.Rendering.Package;

namespace RufusMapEditor.App.Services;

/// <summary>
/// RUFUS library access: discover maps, load MapData + SWF metadata, render.
/// Discovery: Maps/{id}/ with {id}.rufmap (official) and/or {id}.sql (legacy). Unique MapId.
/// </summary>
public sealed class AstriaLibraryService : IDisposable
{
    private CachedBitmapGfxProvider? _imageCache;
    private IGfxCatalog? _catalog;
    private AstriaMapRenderer? _renderer;

    public string? RootPath { get; private set; }
    public bool IsLoaded => RootPath is not null && _catalog is not null;
    public IGfxCatalog? Catalog => _catalog;
    /// <summary>Shared map renderer when library is loaded (used by package export).</summary>
    public AstriaMapRenderer? Renderer => _renderer;

    public void Dispose()
    {
        _imageCache?.Dispose();
        _imageCache = null;
        _renderer = null;
        _catalog = null;
        RootPath = null;
    }

    public void Unload()
    {
        Dispose();
    }

    /// <summary>
    /// Loads GFX catalog from an Astria installation root. Does not modify library files.
    /// </summary>
    public void LoadLibrary(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Biblioteca no encontrada: {rootPath}");

        var mapsDir = Path.Combine(rootPath, "Maps");
        if (!Directory.Exists(mapsDir))
            throw new DirectoryNotFoundException($"No se encontró la carpeta Maps en: {rootPath}");

        _imageCache?.Dispose();
        _imageCache = new CachedBitmapGfxProvider();
        var built = AstriaGfxCatalogBuilder.Build(rootPath);
        _catalog = built.Catalog;
        _renderer = new AstriaMapRenderer(_catalog, _imageCache);
        RootPath = rootPath;
    }

    /// <summary>
    /// Discovers map IDs from Maps/{id}/ folders that contain {id}.rufmap and/or {id}.sql.
    /// Skips staging sidecars (.id.tmp- / .id.old-). Does not treat .png/.txt as map sources.
    /// </summary>
    public IReadOnlyList<int> DiscoverMapIds()
    {
        if (RootPath is null)
            return Array.Empty<int>();

        var mapsDir = LibraryMapPaths.GetMapsRoot(RootPath);
        if (!Directory.Exists(mapsDir))
            return Array.Empty<int>();

        var ids = new HashSet<int>();
        foreach (var dir in Directory.EnumerateDirectories(mapsDir))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.'))
                continue;
            if (!int.TryParse(name, out var id) || id <= 0)
                continue;

            var rufmap = Path.Combine(dir, $"{id}.rufmap");
            var sql = Path.Combine(dir, $"{id}.sql");
            if (File.Exists(rufmap) || File.Exists(sql))
                ids.Add(id);
        }

        var list = ids.ToList();
        list.Sort();
        return list;
    }

    public MapDocument LoadMapDocument(int mapId) => LoadMapDocument(mapId, out _);

    public MapDocument LoadMapDocument(int mapId, out FlasmSwfMetadataReader.SwfMapMetadata? swfMeta)
    {
        swfMeta = null;
        if (RootPath is null)
            throw new InvalidOperationException("No hay biblioteca cargada.");

        var mapFolder = LibraryMapPaths.GetOfficialMapDirectory(RootPath, mapId);
        var rufmapPath = Path.Combine(mapFolder, $"{mapId}.rufmap");
        var sqlPath = Path.Combine(mapFolder, $"{mapId}.sql");

        MapDocument map;
        if (File.Exists(rufmapPath))
        {
            // Official RUFUS save preferred over legacy SQL in the same folder.
            var loaded = RufmapIo.LoadFile(rufmapPath);
            map = loaded.Document;
            FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
        }
        else if (File.Exists(sqlPath))
        {
            map = AstriaSqlMapParser.ParseFile(sqlPath);
            map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();
            FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
        }
        else
        {
            throw new FileNotFoundException(
                $"Mapa {mapId} no encontrado (ni {mapId}.rufmap ni {mapId}.sql) en: {mapFolder}");
        }

        // Prefer explicit official AME SWF; else legacy preferred SWF in folder.
        var officialSwf = Path.Combine(mapFolder, $"{mapId}_AME.swf");
        var swf = File.Exists(officialSwf)
            ? officialSwf
            : FlasmSwfMetadataReader.ResolvePreferredSwf(mapFolder, mapId);
        if (swf is not null)
        {
            var flasm = Path.Combine(RootPath, "Flasm", "flasm.exe");
            if (File.Exists(flasm))
            {
                try
                {
                    swfMeta = FlasmSwfMetadataReader.Read(swf, flasm);
                    // Only fill missing Outdoor/metadata from SWF when loading legacy SQL.
                    // Official .rufmap already carries editable state (incl. FightPlaces).
                    if (!File.Exists(rufmapPath))
                        FlasmSwfMetadataReader.ApplyToDocument(map, swfMeta);
                    else if (map.Outdoor is null)
                        FlasmSwfMetadataReader.ApplyToDocument(map, swfMeta);
                }
                catch
                {
                    // SWF metadata is optional.
                }
            }
        }

        return map;
    }

    public MapRenderResult Render(MapDocument map, MapRenderOptions? options = null)
    {
        if (_renderer is null)
            throw new InvalidOperationException("No hay biblioteca cargada.");

        return _renderer.Render(map, options ?? new MapRenderOptions
        {
            AstriaLogoPath = null,
            CropToExportBounds = true,
        });
    }

    public IReadOnlyList<int> DiscoverBackgroundIds()
    {
        if (_catalog is null) return Array.Empty<int>();
        return _catalog.Enumerate(GfxCategory.Background).Select(r => r.Id).OrderBy(id => id).ToList();
    }
}
