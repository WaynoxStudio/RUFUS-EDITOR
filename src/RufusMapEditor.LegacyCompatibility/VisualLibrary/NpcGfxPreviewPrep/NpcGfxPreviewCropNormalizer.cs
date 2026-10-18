using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>
/// ADMIN.UI.4B.2A.3G.2 — crop transparent excess, fit aspect, center on square canvas.
/// Dev-prep only. Does not change <see cref="VisualImageNormalizer"/> (runtime import path).
/// Canvas size reuses <see cref="PortableVisualStore.MaxPreviewEdge"/> (256).
/// Content fill 85% → ~7.5% transparent margin each side when bounds allow.
/// </summary>
public static class NpcGfxPreviewCropNormalizer
{
    public const int CanvasSize = PortableVisualStore.MaxPreviewEdge; // 256 — project preview edge
    public const float ContentFillRatio = 0.85f;

    public sealed class CropResult
    {
        public required bool HasContent { get; init; }
        public int SourceWidth { get; init; }
        public int SourceHeight { get; init; }
        public int BoundX { get; init; }
        public int BoundY { get; init; }
        public int BoundWidth { get; init; }
        public int BoundHeight { get; init; }
        public int CanvasWidth { get; init; }
        public int CanvasHeight { get; init; }
        public required byte[] PngBytes { get; init; }
    }

    public static CropResult NormalizeToPngBytes(
        Bitmap source,
        int canvasSize = CanvasSize,
        float contentFillRatio = ContentFillRatio)
    {
        canvasSize = Math.Clamp(canvasSize, 32, 1024);
        contentFillRatio = Math.Clamp(contentFillRatio, 0.5f, 0.95f);

        var bounds = MeasureVisibleBounds(source);
        if (!bounds.HasContent)
        {
            using var empty = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);
            using var gEmpty = Graphics.FromImage(empty);
            gEmpty.Clear(Color.Transparent);
            return new CropResult
            {
                HasContent = false,
                SourceWidth = source.Width,
                SourceHeight = source.Height,
                CanvasWidth = canvasSize,
                CanvasHeight = canvasSize,
                PngBytes = EncodePng(empty),
            };
        }

        var cropRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        using var cropped = source.Clone(cropRect, PixelFormat.Format32bppArgb);

        var usable = canvasSize * contentFillRatio;
        var scale = Math.Min(usable / cropped.Width, usable / cropped.Height);
        var drawW = Math.Max(1, (int)Math.Round(cropped.Width * scale));
        var drawH = Math.Max(1, (int)Math.Round(cropped.Height * scale));
        var offsetX = (canvasSize - drawW) / 2;
        var offsetY = (canvasSize - drawH) / 2;

        using var canvas = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(cropped, new Rectangle(offsetX, offsetY, drawW, drawH));
        }

        return new CropResult
        {
            HasContent = true,
            SourceWidth = source.Width,
            SourceHeight = source.Height,
            BoundX = bounds.X,
            BoundY = bounds.Y,
            BoundWidth = bounds.Width,
            BoundHeight = bounds.Height,
            CanvasWidth = canvasSize,
            CanvasHeight = canvasSize,
            PngBytes = EncodePng(canvas),
        };
    }

    public static CropResult NormalizeFile(string sourcePngPath, string destPngPath,
        int canvasSize = CanvasSize, float contentFillRatio = ContentFillRatio)
    {
        using var src = new Bitmap(sourcePngPath);
        var result = NormalizeToPngBytes(src, canvasSize, contentFillRatio);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destPngPath))!);
        File.WriteAllBytes(destPngPath, result.PngBytes);
        return result;
    }

    public readonly struct VisibleBounds
    {
        public required bool HasContent { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    public static VisibleBounds MeasureVisibleBounds(Bitmap bmp)
    {
        using var argb = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(argb))
            g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);

        var rect = new Rectangle(0, 0, argb.Width, argb.Height);
        var data = argb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * argb.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var stride = Math.Abs(data.Stride);
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < argb.Height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < argb.Width; x++)
                {
                    if (bytes[row + x * 4 + 3] < NpcGfxPngContentValidator.AlphaThreshold)
                        continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0)
                return new VisibleBounds { HasContent = false };

            return new VisibleBounds
            {
                HasContent = true,
                X = minX,
                Y = minY,
                Width = maxX - minX + 1,
                Height = maxY - minY + 1,
            };
        }
        finally
        {
            argb.UnlockBits(data);
        }
    }

    private static byte[] EncodePng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
