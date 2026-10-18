using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Local visual match against catalog PNGs.
/// Color-first (histogram + mean) then structure — no AI / network / tokens.
/// </summary>
public static class GfxVisualSearch
{
    private const int Grid = 32;
    private const int HistBins = 8; // 8^3 = 512 RGB bins
    private const byte AlphaMin = 40;

    public sealed record Match(
        GfxResource Resource,
        double Score,
        ImageSource? Thumbnail);

    public static WriteableBitmap? LoadBitmap(string path, int? decodeMax = 256)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeMax is int max)
                bi.DecodePixelWidth = max;
            bi.EndInit();
            bi.Freeze();
            return ToWritable(bi);
        }
        catch
        {
            return null;
        }
    }

    public static WriteableBitmap? FromClipboard()
    {
        if (!Clipboard.ContainsImage()) return null;
        try
        {
            var src = Clipboard.GetImage();
            return src is null ? null : ToWritable(src);
        }
        catch
        {
            return null;
        }
    }

    public static WriteableBitmap? FromBitmapSource(BitmapSource? src) =>
        src is null ? null : ToWritable(src);

    public static WriteableBitmap Crop(BitmapSource source, Int32Rect rect)
    {
        rect = ClampRect(source, rect);
        var cropped = new CroppedBitmap(source, rect);
        cropped.Freeze();
        return ToWritable(cropped);
    }

    public static IReadOnlyList<Match> Search(
        BitmapSource query,
        IEnumerable<GfxResource> catalog,
        GfxThumbnailCache thumbs,
        int topN = 24,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (!TryBuildSignature(query, out var querySig))
            return Array.Empty<Match>();

        var scored = new List<(GfxResource Res, double Score)>();
        var list = catalog.Where(r => File.Exists(r.FilePath)).ToList();
        var i = 0;
        foreach (var res in list)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            if (i % 20 == 0)
                progress?.Report(i * 100 / Math.Max(1, list.Count));

            // Keep enough detail for color; catalog tiles are often small.
            var bmp = LoadBitmap(res.FilePath, decodeMax: 160);
            if (bmp is null) continue;
            if (!TryBuildSignature(bmp, out var candSig))
                continue;

            // Hard reject on color — avoids beige stone matching green grass.
            var meanDist = ColorDistance(querySig.MeanR, querySig.MeanG, querySig.MeanB,
                candSig.MeanR, candSig.MeanG, candSig.MeanB);
            if (meanDist > 95)
                continue;

            var histScore = HistogramIntersection(querySig.Hist, candSig.Hist);
            if (histScore < 0.42)
                continue;

            var structScore = StructureScore(querySig, candSig);
            var meanScore = 1.0 - meanDist / 255.0;

            // Color dominates; structure refines among similar hues.
            var score = 0.40 * histScore + 0.30 * meanScore + 0.30 * structScore;
            if (score >= 0.62)
                scored.Add((res, score));
        }

        progress?.Report(100);

        return scored
            .OrderByDescending(s => s.Score)
            .Take(topN)
            .Select(s => new Match(s.Res, s.Score, thumbs.GetThumbnail(s.Res, 72)))
            .ToList();
    }

    private struct Signature
    {
        public float[] Hist;
        public float MeanR;
        public float MeanG;
        public float MeanB;
        public float[] GridR;
        public float[] GridG;
        public float[] GridB;
        public float[] GridL;
        public float[] Edge;
    }

    private static bool TryBuildSignature(BitmapSource source, out Signature sig)
    {
        sig = default;
        var pixels = GetBgra(source, out var w, out var h);
        if (pixels is null || w < 2 || h < 2)
            return false;

        // Opaque bounding box (catalog tiles are diamonds on transparent).
        var minX = w;
        var minY = h;
        var maxX = -1;
        var maxY = -1;
        var opaque = 0;
        double sumR = 0, sumG = 0, sumB = 0;
        var hist = new float[HistBins * HistBins * HistBins];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                if (pixels[o + 3] < AlphaMin) continue;
                opaque++;
                var b = pixels[o];
                var g = pixels[o + 1];
                var r = pixels[o + 2];
                sumR += r;
                sumG += g;
                sumB += b;
                hist[HistIndex(r, g, b)]++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (opaque < 16 || maxX < minX || maxY < minY)
            return false;

        NormalizeHist(hist);

        var bw = maxX - minX + 1;
        var bh = maxY - minY + 1;
        var gridR = new float[Grid * Grid];
        var gridG = new float[Grid * Grid];
        var gridB = new float[Grid * Grid];
        var gridL = new float[Grid * Grid];
        var counts = new int[Grid * Grid];

        for (var y = minY; y <= maxY; y++)
        {
            var gy = Math.Min(Grid - 1, (y - minY) * Grid / bh);
            for (var x = minX; x <= maxX; x++)
            {
                var o = (y * w + x) * 4;
                if (pixels[o + 3] < AlphaMin) continue;
                var gx = Math.Min(Grid - 1, (x - minX) * Grid / bw);
                var idx = gy * Grid + gx;
                var b = pixels[o];
                var g = pixels[o + 1];
                var r = pixels[o + 2];
                gridR[idx] += r;
                gridG[idx] += g;
                gridB[idx] += b;
                gridL[idx] += 0.299f * r + 0.587f * g + 0.114f * b;
                counts[idx]++;
            }
        }

        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
            {
                // Empty cell → neutral gray (not white) so transparency doesn't dominate.
                gridR[i] = gridG[i] = gridB[i] = 128;
                gridL[i] = 128;
                continue;
            }

            var c = counts[i];
            gridR[i] /= c;
            gridG[i] /= c;
            gridB[i] /= c;
            gridL[i] /= c;
        }

        var edge = new float[Grid * Grid];
        for (var y = 0; y < Grid; y++)
        {
            for (var x = 0; x < Grid; x++)
            {
                var i = y * Grid + x;
                var dx = x + 1 < Grid ? Math.Abs(gridL[i] - gridL[i + 1]) : 0;
                var dy = y + 1 < Grid ? Math.Abs(gridL[i] - gridL[i + Grid]) : 0;
                edge[i] = dx + dy;
            }
        }

        sig = new Signature
        {
            Hist = hist,
            MeanR = (float)(sumR / opaque),
            MeanG = (float)(sumG / opaque),
            MeanB = (float)(sumB / opaque),
            GridR = gridR,
            GridG = gridG,
            GridB = gridB,
            GridL = gridL,
            Edge = edge,
        };
        return true;
    }

    private static double StructureScore(in Signature a, in Signature b)
    {
        var colorCorr = (
            Corr(a.GridR, b.GridR) +
            Corr(a.GridG, b.GridG) +
            Corr(a.GridB, b.GridB)) / 3.0;
        var lumCorr = Corr(a.GridL, b.GridL);
        var edgeCorr = Corr(a.Edge, b.Edge);
        return 0.50 * colorCorr + 0.25 * lumCorr + 0.25 * edgeCorr;
    }

    private static double Corr(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        if (n == 0) return 0;
        double meanA = 0, meanB = 0;
        for (var i = 0; i < n; i++)
        {
            meanA += a[i];
            meanB += b[i];
        }

        meanA /= n;
        meanB /= n;
        double num = 0, denA = 0, denB = 0;
        for (var i = 0; i < n; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            num += da * db;
            denA += da * da;
            denB += db * db;
        }

        var den = Math.Sqrt(denA * denB);
        if (den < 1e-6) return 0;
        return Math.Clamp((num / den + 1.0) * 0.5, 0, 1);
    }

    private static double HistogramIntersection(float[] a, float[] b)
    {
        double sum = 0;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
            sum += Math.Min(a[i], b[i]);
        return Math.Clamp(sum, 0, 1);
    }

    private static double ColorDistance(float r1, float g1, float b1, float r2, float g2, float b2)
    {
        // Weighted RGB distance (emphasize green/red differences for terrain).
        var dr = r1 - r2;
        var dg = g1 - g2;
        var db = b1 - b2;
        return Math.Sqrt(0.30 * dr * dr + 0.59 * dg * dg + 0.11 * db * db);
    }

    private static int HistIndex(byte r, byte g, byte b)
    {
        var ri = Math.Min(HistBins - 1, r * HistBins / 256);
        var gi = Math.Min(HistBins - 1, g * HistBins / 256);
        var bi = Math.Min(HistBins - 1, b * HistBins / 256);
        return (ri * HistBins + gi) * HistBins + bi;
    }

    private static void NormalizeHist(float[] hist)
    {
        double sum = 0;
        for (var i = 0; i < hist.Length; i++)
            sum += hist[i];
        if (sum < 1e-6) return;
        for (var i = 0; i < hist.Length; i++)
            hist[i] = (float)(hist[i] / sum);
    }

    private static byte[]? GetBgra(BitmapSource source, out int w, out int h)
    {
        w = source.PixelWidth;
        h = source.PixelHeight;
        try
        {
            BitmapSource bgra = source;
            if (source.Format != PixelFormats.Bgra32)
            {
                bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                bgra.Freeze();
            }

            var stride = w * 4;
            var pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);
            return pixels;
        }
        catch
        {
            return null;
        }
    }

    private static WriteableBitmap ToWritable(BitmapSource source)
    {
        if (source.Format != PixelFormats.Bgra32)
        {
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            source.Freeze();
        }

        var wb = new WriteableBitmap(source);
        wb.Freeze();
        return wb;
    }

    private static Int32Rect ClampRect(BitmapSource source, Int32Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, source.PixelWidth - 1);
        var y = Math.Clamp(rect.Y, 0, source.PixelHeight - 1);
        var w = Math.Clamp(rect.Width, 1, source.PixelWidth - x);
        var h = Math.Clamp(rect.Height, 1, source.PixelHeight - y);
        return new Int32Rect(x, y, w, h);
    }
}
