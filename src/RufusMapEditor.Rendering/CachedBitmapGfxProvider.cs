using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.Rendering;

/// <summary>
/// Decodes GFX files once per (category, id) for the lifetime of a render session.
/// </summary>
public sealed class CachedBitmapGfxProvider : IDisposable
{
    private readonly ConcurrentDictionary<(GfxCategory Category, int Id), Bitmap> _cache = new();
    private readonly ConcurrentDictionary<(GfxCategory Category, int Id), byte> _loadFailures = new();
    private bool _disposed;

    public int UniqueImagesLoaded => _cache.Count;

    public bool TryGetBitmap(GfxResource resource, out Bitmap bitmap)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(resource);

        bitmap = null!;
        var key = (resource.Category, resource.Id);
        if (_loadFailures.ContainsKey(key))
            return false;

        if (_cache.TryGetValue(key, out var cached))
        {
            bitmap = cached;
            return true;
        }

        try
        {
            // Copy into an independent Bitmap before disposing the stream (GDI+ requirement).
            using var stream = new FileStream(resource.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var fromFile = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            var loaded = new Bitmap(fromFile);
            if (loaded.Width <= 0 || loaded.Height <= 0)
            {
                loaded.Dispose();
                _loadFailures[key] = 0;
                return false;
            }

            bitmap = _cache.GetOrAdd(key, _ => loaded);
            if (!ReferenceEquals(bitmap, loaded))
                loaded.Dispose();
            return true;
        }
        catch
        {
            _loadFailures[key] = 0;
            return false;
        }
    }

    public Bitmap GetBitmap(GfxResource resource)
    {
        if (!TryGetBitmap(resource, out var bitmap))
            throw new InvalidOperationException(
                $"Cannot decode GFX image {resource.Category}:{resource.Id} ({resource.FilePath}).");
        return bitmap;
    }

    public bool TryCloneWorkingCopy(GfxResource resource, out Bitmap clone)
    {
        clone = null!;
        if (!TryGetBitmap(resource, out var source))
            return false;

        try
        {
            clone = (Bitmap)source.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Bitmap CloneWorkingCopy(GfxResource resource)
    {
        if (!TryCloneWorkingCopy(resource, out var clone))
            throw new InvalidOperationException(
                $"Cannot decode GFX image {resource.Category}:{resource.Id} ({resource.FilePath}).");
        return clone;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var bmp in _cache.Values)
            bmp.Dispose();
        _cache.Clear();
        _loadFailures.Clear();
    }
}
