using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RufusMapEditor.Rendering;
using RufusMapEditor.Rendering.Package;

namespace RufusMapEditor.App.Services;

public sealed record MapPreviewInfo(ImageSource Image, int Width, int Height);

/// <summary>
/// Lazy map thumbnails for the library map list, isla preview, and hover.
/// Prefers the official Library PNG on disk (persistent cache); renders only when missing.
/// </summary>
public sealed class MapPreviewCache
{
    private readonly ConcurrentDictionary<int, MapPreviewInfo> _cache = new();
    private readonly ConcurrentDictionary<int, Task<MapPreviewInfo?>> _inflight = new();

    private static readonly MapRenderOptions DefaultOptions = new()
    {
        AstriaLogoPath = null,
        CropToExportBounds = true,
        DrawBackground = true,
        DrawGround = true,
        DrawObjectLayer1 = true,
        DrawObjectLayer2 = true,
    };

    public MapPreviewInfo? TryGetCached(int mapId) =>
        _cache.TryGetValue(mapId, out var cached) ? cached : null;

    public Task<MapPreviewInfo?> GetOrRenderAsync(AstriaLibraryService library, int mapId) =>
        GetOrRenderAsync(library, mapId, DefaultOptions);

    public Task<MapPreviewInfo?> GetOrRenderAsync(AstriaLibraryService library, int mapId, MapRenderOptions options)
    {
        if (_cache.TryGetValue(mapId, out var cached))
            return Task.FromResult<MapPreviewInfo?>(cached);

        return _inflight.GetOrAdd(mapId, _ => RenderAsync(library, mapId, options));
    }

    private async Task<MapPreviewInfo?> RenderAsync(AstriaLibraryService library, int mapId, MapRenderOptions options)
    {
        try
        {
            return await Task.Run(() =>
            {
                if (_cache.TryGetValue(mapId, out var cached))
                    return cached;

                // Persistent cache: official Library\Maps\{id}\{id}.png written by Official Map Save.
                if (!string.IsNullOrWhiteSpace(library.RootPath))
                {
                    var fromDisk = TryLoadOfficialPng(library.RootPath, mapId);
                    if (fromDisk is not null)
                    {
                        _cache[mapId] = fromDisk;
                        return fromDisk;
                    }
                }

                if (!library.IsLoaded)
                    return null;

                var doc = library.LoadMapDocument(mapId);
                var result = library.Render(doc, options);
                try
                {
                    var src = BitmapConversion.ToBitmapSource(result.Image);
                    var info = new MapPreviewInfo(src, doc.Width, doc.Height);
                    _cache[mapId] = info;

                    // Persist so next launch / isla strip / mundo don't re-render.
                    TrySaveOfficialPng(library.RootPath, mapId, result.Image);
                    return info;
                }
                finally
                {
                    result.Image.Dispose();
                }
            });
        }
        catch (Exception)
        {
            // Corrupt/legacy maps must not crash hover preview or the UI dispatcher.
            return null;
        }
        finally
        {
            _inflight.TryRemove(mapId, out _);
        }
    }

    /// <summary>Load official PNG into memory cache without rendering (sync, for world redraw).</summary>
    public MapPreviewInfo? TryLoadFromDisk(AstriaLibraryService library, int mapId)
    {
        if (_cache.TryGetValue(mapId, out var cached))
            return cached;
        if (string.IsNullOrWhiteSpace(library.RootPath))
            return null;
        var info = TryLoadOfficialPng(library.RootPath, mapId);
        if (info is not null)
            _cache[mapId] = info;
        return info;
    }

    private static MapPreviewInfo? TryLoadOfficialPng(string libraryRoot, int mapId)
    {
        try
        {
            var path = LibraryMapPaths.GetOfficialPngPath(libraryRoot, mapId);
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
                return null;

            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return new MapPreviewInfo(bi, bi.PixelWidth, bi.PixelHeight);
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveOfficialPng(string? libraryRoot, int mapId, System.Drawing.Bitmap bitmap)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot) || mapId <= 0 || bitmap is null)
            return;
        try
        {
            var path = LibraryMapPaths.GetOfficialPngPath(libraryRoot, mapId);
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir))
                return;
            // Don't overwrite an existing official export.
            if (File.Exists(path))
                return;
            Directory.CreateDirectory(dir);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch
        {
            // Best-effort disk cache.
        }
    }

    public void Invalidate(int mapId)
    {
        _cache.TryRemove(mapId, out _);
        _inflight.TryRemove(mapId, out _);
    }

    public void Clear()
    {
        _cache.Clear();
        _inflight.Clear();
    }
}
