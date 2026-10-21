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
    private readonly ConcurrentDictionary<(GfxCategory Category, int Id, int DecodeWidth, long WriteTicks), ImageSource> _cache = new();

    public ImageSource? GetThumbnail(GfxResource resource, int decodeWidth = 64)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (string.IsNullOrWhiteSpace(resource.FilePath))
            return null;

        try
        {
            var writeTicks = File.Exists(resource.FilePath)
                ? File.GetLastWriteTimeUtc(resource.FilePath).Ticks
                : 0L;
            return _cache.GetOrAdd((resource.Category, resource.Id, decodeWidth, writeTicks), _ =>
            {
                // BitmapImage + StreamSource (no Uri) crashes WPF ImagingCache
                // with "Value cannot be null (Parameter 'key')". Load from URI and
                // bust the WPF image cache with IgnoreImageCache + writeTicks in our key.
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(Path.GetFullPath(resource.FilePath), UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                bi.DecodePixelWidth = decodeWidth;
                bi.EndInit();
                bi.Freeze();
                return bi;
            });
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void Clear() => _cache.Clear();
}
