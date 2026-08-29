using System.Drawing;
using System.Drawing.Imaging;

namespace RufusMapEditor.Rendering;

public sealed class ImageComparisonResult
{
    public required int WidthA { get; init; }
    public required int HeightA { get; init; }
    public required int WidthB { get; init; }
    public required int HeightB { get; init; }
    public bool SameDimensions => WidthA == WidthB && HeightA == HeightB;
    public required long DifferentPixels { get; init; }
    public required long TotalPixelsCompared { get; init; }
    public double DifferentPercent => TotalPixelsCompared == 0 ? 0 : 100.0 * DifferentPixels / TotalPixelsCompared;
    public required double MeanAbsDifference { get; init; }
    public required int MaxChannelDifference { get; init; }
    public required Rectangle? DiffBoundingBox { get; init; }
    public bool Identical => SameDimensions && DifferentPixels == 0;
}

/// <summary>
/// Pixel comparison utilities for golden-master PNG checks.
/// </summary>
public static class ImageComparer
{
    public static ImageComparisonResult Compare(Bitmap a, Bitmap b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Width != b.Width || a.Height != b.Height)
        {
            return new ImageComparisonResult
            {
                WidthA = a.Width,
                HeightA = a.Height,
                WidthB = b.Width,
                HeightB = b.Height,
                DifferentPixels = (long)a.Width * a.Height,
                TotalPixelsCompared = (long)a.Width * a.Height,
                MeanAbsDifference = 255,
                MaxChannelDifference = 255,
                DiffBoundingBox = new Rectangle(0, 0, a.Width, a.Height),
            };
        }

        var w = a.Width;
        var h = a.Height;
        long different = 0;
        double absSum = 0;
        var maxDiff = 0;
        var minX = w;
        var minY = h;
        var maxX = -1;
        var maxY = -1;

        // Lock bits for speed
        var rect = new Rectangle(0, 0, w, h);
        var dataA = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dataB = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var pa = (byte*)dataA.Scan0;
                var pb = (byte*)dataB.Scan0;
                var strideA = dataA.Stride;
                var strideB = dataB.Stride;

                for (var y = 0; y < h; y++)
                {
                    var rowA = pa + y * strideA;
                    var rowB = pb + y * strideB;
                    for (var x = 0; x < w; x++)
                    {
                        var i = x * 4;
                        var db = Math.Abs(rowA[i] - rowB[i]);
                        var dg = Math.Abs(rowA[i + 1] - rowB[i + 1]);
                        var dr = Math.Abs(rowA[i + 2] - rowB[i + 2]);
                        var da = Math.Abs(rowA[i + 3] - rowB[i + 3]);
                        var channelMax = Math.Max(Math.Max(db, dg), Math.Max(dr, da));
                        absSum += (db + dg + dr + da) / 4.0;
                        if (channelMax > 0)
                        {
                            different++;
                            if (channelMax > maxDiff) maxDiff = channelMax;
                            if (x < minX) minX = x;
                            if (y < minY) minY = y;
                            if (x > maxX) maxX = x;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
        }
        finally
        {
            a.UnlockBits(dataA);
            b.UnlockBits(dataB);
        }

        var total = (long)w * h;
        Rectangle? bbox = different == 0 ? null : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);

        return new ImageComparisonResult
        {
            WidthA = w,
            HeightA = h,
            WidthB = b.Width,
            HeightB = b.Height,
            DifferentPixels = different,
            TotalPixelsCompared = total,
            MeanAbsDifference = total == 0 ? 0 : absSum / total,
            MaxChannelDifference = maxDiff,
            DiffBoundingBox = bbox,
        };
    }

    public static Bitmap CreateDiffImage(Bitmap a, Bitmap b)
    {
        var w = Math.Min(a.Width, b.Width);
        var h = Math.Min(a.Height, b.Height);
        var diff = new Bitmap(w, h, PixelFormat.Format32bppArgb);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var ca = a.GetPixel(x, y);
                var cb = b.GetPixel(x, y);
                if (ca.ToArgb() == cb.ToArgb())
                    diff.SetPixel(x, y, Color.FromArgb(255, 20, 20, 20));
                else
                    diff.SetPixel(x, y, Color.FromArgb(255, 255, 0, 0));
            }
        }

        return diff;
    }
}
