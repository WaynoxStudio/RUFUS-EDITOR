using System.Drawing;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal readonly record struct SwfComposeAnalysis(
    int ActiveDepths,
    int ShapesDrawn,
    int NestedSprites,
    int BitmapsDrawn,
    int IgnoredSymbols,
    RectangleF RawBounds,
    RectangleF VisibleBounds);

internal static class SwfBitmapBounds
{
    public static Rectangle CropToVisiblePixels(Bitmap bmp, byte alphaThreshold = 8)
    {
        if (bmp.Width <= 0 || bmp.Height <= 0)
            return Rectangle.Empty;

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var bytes = stride * bmp.Height;
            var buf = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, bytes);

            var minX = bmp.Width;
            var minY = bmp.Height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < bmp.Height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < bmp.Width; x++)
                {
                    if (buf[row + x * 4 + 3] <= alphaThreshold)
                        continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return Rectangle.Empty;

            return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public static Bitmap CropCopy(Bitmap source, Rectangle crop)
    {
        if (crop.Width <= 0 || crop.Height <= 0)
            return new Bitmap(1, 1, source.PixelFormat);
        var dst = new Bitmap(crop.Width, crop.Height, source.PixelFormat);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(source, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
        return dst;
    }
}
