using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Rendering;
using RufusMapEditor.Rendering.Package;

namespace RufusMapEditor.App.Services;

public sealed class WorldThumbnailCache : IDisposable
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new();
    private MapPreviewCache? _mapPreviews;

    public void AttachMapPreviewCache(MapPreviewCache mapPreviews) =>
        _mapPreviews = mapPreviews;

    public static string Fingerprint(MapDocument map, MapRenderOptions? options = null)
    {
        // Do not re-encode MapData here — Invalidate() already drops stale thumbs after edits.
        var data = map.MapData ?? string.Empty;
        var baseKey = $"{map.Id}:{map.Width}x{map.Height}:{data.Length}:{data.GetHashCode()}";
        if (options is null) return baseKey;
        return $"{baseKey}|bg{options.DrawBackground}|g{options.DrawGround}|o1{options.DrawObjectLayer1}|o2{options.DrawObjectLayer2}";
    }

    public ImageSource? GetOrRender(AstriaLibraryService library, MapDocument map, MapRenderOptions? options = null)
    {
        var key = Fingerprint(map, options);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // Prefer shared map-preview cache / official PNG (fast, persistent).
        if (options is null)
        {
            var fromPreview = _mapPreviews?.TryGetCached(map.Id)
                ?? _mapPreviews?.TryLoadFromDisk(library, map.Id);
            if (fromPreview?.Image is not null)
            {
                _cache[key] = fromPreview.Image;
                return fromPreview.Image;
            }

            if (!string.IsNullOrWhiteSpace(library.RootPath))
            {
                var fromDisk = TryLoadOfficialPng(library.RootPath, map.Id);
                if (fromDisk is not null)
                {
                    _cache[key] = fromDisk;
                    return fromDisk;
                }
            }
        }

        if (!library.IsLoaded)
            return null;

        try
        {
            var result = library.Render(map, options);
            try
            {
                var src = BitmapConversion.ToBitmapSource(result.Image);
                _cache[key] = src;
                return src;
            }
            finally
            {
                result.Image.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryLoadOfficialPng(string libraryRoot, int mapId)
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
            return bi;
        }
        catch
        {
            return null;
        }
    }

    public void Invalidate(MapDocument map)
    {
        _cache.Keys.Where(k => k.StartsWith($"{map.Id}:{map.Width}x{map.Height}", StringComparison.Ordinal))
            .ToList()
            .ForEach(k => _cache.TryRemove(k, out _));
        // Drop shared Library PNG memory cache so next paint/redraw reloads from disk.
        _mapPreviews?.Invalidate(map.Id);
    }

    public void Clear() => _cache.Clear();

    public void Dispose() => _cache.Clear();
}
