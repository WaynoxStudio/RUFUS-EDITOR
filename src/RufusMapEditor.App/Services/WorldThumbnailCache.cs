using System.Collections.Concurrent;
using System.Windows.Media;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

public sealed class WorldThumbnailCache : IDisposable
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new();

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

    public void Invalidate(MapDocument map) =>
        _cache.Keys.Where(k => k.StartsWith($"{map.Id}:{map.Width}x{map.Height}", StringComparison.Ordinal))
            .ToList()
            .ForEach(k => _cache.TryRemove(k, out _));

    public void Clear() => _cache.Clear();

    public void Dispose() => _cache.Clear();
}
