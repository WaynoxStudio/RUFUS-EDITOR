using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using SkiaSharp;
using Svg.Skia;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>
/// CONTENT.ITEMS.SVG_RASTERIZER.INTEGRATE.1 — SVG → PNG via Svg.Skia (Inkscape substitute for FullRootSvg).
/// Canvas = Stage viewBox ∪ ExpandResult.ContentBounds ∪ CullRect + margin (+ optional edge-touch expand).
/// </summary>
public static class SvgSkiaFrameRasterizer
{
    public const float DefaultMargin = 4f;
    public const float MaxBitmapEdge = 2048f;

    public sealed class RasterResult
    {
        public required bool Ok { get; init; }
        public string? PngPath { get; init; }
        public string? Error { get; init; }
        public string? CanvasRect { get; init; }
        public bool EdgeExpanded { get; init; }
        public int OpaquePixelCount { get; init; }
    }

    public readonly record struct StageBox(float MinX, float MinY, float MaxX, float MaxY);

    /// <summary>
    /// Rasterize FFDec frame SVG using validated bounds. Prefer <paramref name="originalSvgPath"/>
    /// (Skia paints in user/stage space). <paramref name="contentBounds"/> from expander user-space AABB.
    /// </summary>
    public static RasterResult RasterizeToPng(
        string originalSvgPath,
        string outputPngPath,
        SvgFrameBoundsExpander.Bounds? contentBounds,
        float margin = DefaultMargin,
        string? expandedSvgFallbackPath = null)
    {
        if (string.IsNullOrWhiteSpace(originalSvgPath) || !File.Exists(originalSvgPath))
        {
            return new RasterResult { Ok = false, Error = "SVG original inexistente: " + originalSvgPath };
        }

        var stage = TryReadSvgViewBox(File.ReadAllText(originalSvgPath)) ?? new StageBox(0, 0, 550, 400);
        var result = TryRasterize(originalSvgPath, outputPngPath, contentBounds, stage, margin);
        if (result.Ok && result.OpaquePixelCount >= 20)
            return result;

        if (!string.IsNullOrWhiteSpace(expandedSvgFallbackPath)
            && File.Exists(expandedSvgFallbackPath)
            && !string.Equals(expandedSvgFallbackPath, originalSvgPath, StringComparison.OrdinalIgnoreCase))
        {
            var fallback = TryRasterize(expandedSvgFallbackPath, outputPngPath, contentBounds, stage, margin);
            if (fallback.Ok && fallback.OpaquePixelCount >= 20)
                return fallback;
            return fallback.Ok ? fallback : result;
        }

        return result;
    }

    private static RasterResult TryRasterize(
        string svgPath,
        string pngPath,
        SvgFrameBoundsExpander.Bounds? contentBounds,
        StageBox stage,
        float margin)
    {
        try
        {
            using var svg = new SKSvg();
            svg.Load(svgPath);
            var picture = svg.Picture;
            if (picture is null)
                return new RasterResult { Ok = false, Error = "Svg.Skia Picture null" };

            var cull = picture.CullRect;
            float left = stage.MinX, top = stage.MinY, right = stage.MaxX, bottom = stage.MaxY;

            void Include(float x0, float y0, float x1, float y1)
            {
                left = Math.Min(left, x0);
                top = Math.Min(top, y0);
                right = Math.Max(right, x1);
                bottom = Math.Max(bottom, y1);
            }

            if (!cull.IsEmpty && !float.IsNaN(cull.Width) && cull.Width > 0 && cull.Height > 0)
                Include(cull.Left, cull.Top, cull.Right, cull.Bottom);

            if (contentBounds is { IsValid: true } cb)
                Include((float)cb.MinX, (float)cb.MinY, (float)cb.MaxX, (float)cb.MaxY);

            left -= margin;
            top -= margin;
            right += margin;
            bottom += margin;
            if (right - left < 1) right = left + 1;
            if (bottom - top < 1) bottom = top + 1;

            var edgeExpanded = false;
            using var bmp = Render(picture, left, top, right, bottom);
            var opaque = CountOpaque(bmp);
            if (TouchesEdge(bmp, edgePx: 2) && opaque >= 20)
            {
                var (tL, tT, tR, tB) = EdgeTouchFlags(bmp, 2);
                var padX = Math.Max(32f, (right - left) * 0.08f);
                var padY = Math.Max(32f, (bottom - top) * 0.08f);
                if (tL) left -= padX;
                if (tT) top -= padY;
                if (tR) right += padX;
                if (tB) bottom += padY;
                edgeExpanded = true;
                using var bmp2 = Render(picture, left, top, right, bottom);
                opaque = CountOpaque(bmp2);
                SavePng(bmp2, pngPath);
            }
            else
            {
                SavePng(bmp, pngPath);
            }

            if (!File.Exists(pngPath) || new FileInfo(pngPath).Length <= 0)
                return new RasterResult { Ok = false, Error = "PNG no escrito" };

            return new RasterResult
            {
                Ok = true,
                PngPath = pngPath,
                OpaquePixelCount = opaque,
                EdgeExpanded = edgeExpanded,
                CanvasRect = string.Create(CultureInfo.InvariantCulture,
                    $"{left:0.##},{top:0.##},{right:0.##},{bottom:0.##}"),
            };
        }
        catch (Exception ex)
        {
            return new RasterResult
            {
                Ok = false,
                Error = ex.GetType().Name + ": " + (string.IsNullOrWhiteSpace(ex.Message) ? "(sin mensaje)" : ex.Message),
            };
        }
    }

    private static SKBitmap Render(SKPicture picture, float left, float top, float right, float bottom)
    {
        var fullW = right - left;
        var fullH = bottom - top;
        var scale = 1f;
        if (fullW > MaxBitmapEdge || fullH > MaxBitmapEdge)
            scale = MaxBitmapEdge / Math.Max(fullW, fullH);

        var w = Math.Max(1, (int)Math.Ceiling(fullW * scale));
        var h = Math.Max(1, (int)Math.Ceiling(fullH * scale));
        var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-left, -top);
        canvas.DrawPicture(picture);
        canvas.Flush();
        return bitmap;
    }

    private static void SavePng(SKBitmap bitmap, string pngPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                        ?? throw new InvalidOperationException("Encode PNG null");
        using var fs = File.Open(pngPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(fs);
    }

    private static int CountOpaque(SKBitmap bmp)
    {
        var n = 0;
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
            if (bmp.GetPixel(x, y).Alpha >= 16) n++;
        return n;
    }

    private static bool TouchesEdge(SKBitmap bmp, int edgePx)
    {
        var (l, t, r, b) = EdgeTouchFlags(bmp, edgePx);
        return l || t || r || b;
    }

    private static (bool L, bool T, bool R, bool B) EdgeTouchFlags(SKBitmap bmp, int edgePx)
    {
        edgePx = Math.Clamp(edgePx, 1, 8);
        var left = false;
        var top = false;
        var right = false;
        var bottom = false;
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            if (bmp.GetPixel(x, y).Alpha < 16) continue;
            if (x < edgePx) left = true;
            if (y < edgePx) top = true;
            if (x >= bmp.Width - edgePx) right = true;
            if (y >= bmp.Height - edgePx) bottom = true;
        }

        return (left, top, right, bottom);
    }

    public static StageBox? TryReadSvgViewBox(string svgText)
    {
        try
        {
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(svgText);
            var root = doc.DocumentElement;
            if (root is null) return null;
            var vb = root.GetAttribute("viewBox");
            if (!string.IsNullOrWhiteSpace(vb))
            {
                var parts = Regex.Split(vb.Trim(), @"[\s,]+");
                if (parts.Length >= 4
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                    && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                    && w > 0 && h > 0)
                {
                    return new StageBox(x, y, x + w, y + h);
                }
            }
        }
        catch
        {
            // ignore
        }

        return new StageBox(0, 0, 550, 400);
    }
}
