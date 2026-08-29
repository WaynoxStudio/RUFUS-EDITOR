using System.Collections.Concurrent;
using System.Windows.Media;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

public sealed class WorldThumbnailCache : IDisposable
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new();

    public static string Fingerprint(MapDocument map, MapRenderOptions? options = null)
    {
        MapCellEditor.SyncMapDataString(map);
        var baseKey = $"{map.Id}:{map.Width}x{map.Height}:{map.MapData.Length}:{map.MapData.GetHashCode()}";
        if (options is null) return baseKey;
        return $"{baseKey}|bg{options.DrawBackground}|g{options.DrawGround}|o1{options.DrawObjectLayer1}|o2{options.DrawObjectLayer2}";
    }

    public ImageSource? GetOrRender(AstriaLibraryService library, MapDocument map, MapRenderOptions? options = null)
    {
        var key = Fingerprint(map, options);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        if (!library.IsLoaded)
            return null;

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

    public void Invalidate(MapDocument map) =>
        _cache.Keys.Where(k => k.StartsWith($"{map.Id}:{map.Width}x{map.Height}", StringComparison.Ordinal))
            .ToList()
            .ForEach(k => _cache.TryRemove(k, out _));

    public void Clear() => _cache.Clear();

    public void Dispose() => _cache.Clear();
}
