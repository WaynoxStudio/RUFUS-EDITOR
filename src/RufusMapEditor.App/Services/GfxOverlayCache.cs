using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Cached transformed GFX bitmaps for map overlays (brush preview). Avoids disk IO on every MouseMove.
/// </summary>
public sealed class GfxOverlayCache : IDisposable
{
    private readonly ConcurrentDictionary<OverlayKey, ImageSource?> _cache = new();
    private readonly ConcurrentDictionary<OverlayKey, byte> _failures = new();
    private CachedBitmapGfxProvider _bitmaps = new();

    public ImageSource? GetTransformedImage(
        GfxResource resource,
        bool flip,
        int rotation,
        bool isObject,
        int sizeCell = IsoGeometry.SizeBaseCell)
    {
        if (!File.Exists(resource.FilePath))
            return null;

        var key = new OverlayKey(resource.Category, resource.Id, flip, rotation, isObject, sizeCell);
        if (_failures.ContainsKey(key))
            return null;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var image = CreateImage(resource, flip, rotation, isObject, sizeCell);
            _cache[key] = image;
            return image;
        }
        catch
        {
            _failures[key] = 0;
            return null;
        }
    }

    private ImageSource? CreateImage(GfxResource resource, bool flip, int rotation, bool isObject, int sizeCell)
    {
        if (!_bitmaps.TryGetBitmap(resource, out var source))
            return null;

        var (ax, ay) = GfxPlacementMath.ResolveAnchor(
            resource.Anchor?.X,
            resource.Anchor?.Y,
            source.Width,
            source.Height);

        using var transformed = GfxPlacementMath.TransformBitmap(
            source, ax, ay, flip, rotation, isObject, sizeCell, out var logical);

        var scaled = new Bitmap(logical.Width, logical.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(transformed, 0, 0, logical.Width, logical.Height);
        }

        try
        {
            return ToFrozenBitmapSource(scaled);
        }
        finally
        {
            scaled.Dispose();
        }
    }

    public bool TryComputePlacementInHitSpace(
        IsoHitTester hitTester,
        int cellId,
        GfxResource resource,
        bool flip,
        int rotation,
        bool isObject,
        out GfxPlacementMath.PlacementRect hitRect)
    {
        hitRect = default;
        if (!File.Exists(resource.FilePath))
            return false;

        if (!_bitmaps.TryGetBitmap(resource, out var bmp))
            return false;

        if (!GfxPlacementPipeline.TryBuildFromBitmap(
                hitTester.MapWidth,
                hitTester.MapHeight,
                cellId,
                resource,
                bmp,
                flip,
                rotation,
                isObject,
                out var descriptor))
            return false;

        hitRect = descriptor.HitSpace;
        return true;
    }

    public bool TryBuildPlacementDescriptor(
        IsoHitTester hitTester,
        int cellId,
        GfxResource resource,
        bool flip,
        int rotation,
        bool isObject,
        out GfxPlacementDescriptor descriptor)
    {
        descriptor = null!;
        if (!File.Exists(resource.FilePath))
            return false;
        if (!_bitmaps.TryGetBitmap(resource, out var bmp))
            return false;
        return GfxPlacementPipeline.TryBuildFromBitmap(
            hitTester.MapWidth,
            hitTester.MapHeight,
            cellId,
            resource,
            bmp,
            flip,
            rotation,
            isObject,
            out descriptor);
    }

    private static BitmapSource ToFrozenBitmapSource(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var source = BitmapSource.Create(
                bitmap.Width,
                bitmap.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                data.Scan0,
                data.Stride * bitmap.Height,
                data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public void Clear()
    {
        _cache.Clear();
        _failures.Clear();
        _bitmaps.Dispose();
        _bitmaps = new CachedBitmapGfxProvider();
    }

    public void Dispose()
    {
        _cache.Clear();
        _failures.Clear();
        _bitmaps.Dispose();
    }

    private readonly record struct OverlayKey(
        GfxCategory Category,
        int Id,
        bool Flip,
        int Rotation,
        bool IsObject,
        int SizeCell);
}
