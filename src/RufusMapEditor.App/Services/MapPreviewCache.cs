using System.Collections.Concurrent;
using System.Windows.Media;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

public sealed record MapPreviewInfo(ImageSource Image, int Width, int Height);

/// <summary>
/// Lazy map thumbnails for the library map list (hover preview).
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

                if (!library.IsLoaded)
                    return null;

                var doc = library.LoadMapDocument(mapId);
                var result = library.Render(doc, options);
                try
                {
                    var src = BitmapConversion.ToBitmapSource(result.Image);
                    var info = new MapPreviewInfo(src, doc.Width, doc.Height);
                    _cache[mapId] = info;
                    return info;
                }
                finally
                {
                    result.Image.Dispose();
                }
            });
        }
        finally
        {
            _inflight.TryRemove(mapId, out _);
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
