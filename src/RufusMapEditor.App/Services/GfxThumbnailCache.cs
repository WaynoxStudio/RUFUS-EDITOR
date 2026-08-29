using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Lazy WPF thumbnails for the GFX browser. Does not preload the whole library.
/// </summary>
public sealed class GfxThumbnailCache
{
    private readonly ConcurrentDictionary<(GfxCategory, int, int), ImageSource> _cache = new();

    public ImageSource? GetThumbnail(GfxResource resource, int decodeWidth = 64)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!File.Exists(resource.FilePath))
            return null;

        return _cache.GetOrAdd((resource.Category, resource.Id, decodeWidth), _ =>
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(resource.FilePath, UriKind.Absolute);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.DecodePixelWidth = decodeWidth;
            bi.EndInit();
            bi.Freeze();
            return bi;
        });
    }

    public void Clear() => _cache.Clear();
}
