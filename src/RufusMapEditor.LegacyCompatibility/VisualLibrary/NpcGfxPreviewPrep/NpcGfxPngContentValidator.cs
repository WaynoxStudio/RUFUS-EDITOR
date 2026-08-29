using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>
/// ADMIN.UI.4B.2A.3G.1 — PNG content gate for FFDec exports.
/// Thresholds calibrated on real samples (2026-08-29):
/// <list type="bullet">
/// <item>9059 (good): 550×400, opaque≈9407 (4.28%), visBounds≈120×102</item>
/// <item>1245 (empty frame): 200×200, opaque≈93 (0.23%), visBounds≈16×8</item>
/// </list>
/// Alpha threshold A≥16. Ambiguous band → REVIEW (not silent OK).
/// </summary>
public static class NpcGfxPngContentValidator
{
    /// <summary>Pixels with alpha ≥ this count as opaque/content.</summary>
    public const int AlphaThreshold = 16;

    // Calibrated between 1245 (~93 opaque) and 9059 (~9407 opaque).
    public const int OkMinOpaquePixels = 800;
    public const int OkMinVisibleWidth = 40;
    public const int OkMinVisibleHeight = 40;
    public const int OkMinVisibleArea = 2500;

    public const int FailMaxOpaquePixels = 200;
    public const int FailMaxVisibleWidth = 20;
    public const int FailMaxVisibleHeight = 20;
    public const int FailMaxVisibleArea = 400;

    public static NpcGfxPngValidationResult ValidateFile(string pngPath)
    {
        if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
        {
            return new NpcGfxPngValidationResult
            {
                Decoded = false,
                ContentStatus = NpcGfxPreviewPrepStatus.Failed,
                Reason = "PNG ausente",
            };
        }

        var info = new FileInfo(pngPath);
        try
        {
            using var bmp = new Bitmap(pngPath);
            return ValidateBitmap(bmp, info.Length);
        }
        catch (Exception ex)
        {
            return new NpcGfxPngValidationResult
            {
                Decoded = false,
                FileBytes = info.Length,
                ContentStatus = NpcGfxPreviewPrepStatus.Failed,
                Reason = "PNG no decodificable: " + ex.GetType().Name,
            };
        }
    }

    public static NpcGfxPngValidationResult ValidateBytes(byte[] pngBytes)
    {
        if (pngBytes is null || pngBytes.Length == 0)
        {
            return new NpcGfxPngValidationResult
            {
                Decoded = false,
                ContentStatus = NpcGfxPreviewPrepStatus.Failed,
                Reason = "PNG vacío",
            };
        }

        try
        {
            using var ms = new MemoryStream(pngBytes, writable: false);
            using var bmp = new Bitmap(ms);
            return ValidateBitmap(bmp, pngBytes.Length);
        }
        catch (Exception ex)
        {
            return new NpcGfxPngValidationResult
            {
                Decoded = false,
                FileBytes = pngBytes.Length,
                ContentStatus = NpcGfxPreviewPrepStatus.Failed,
                Reason = "PNG no decodificable: " + ex.GetType().Name,
            };
        }
    }

    public static NpcGfxPngValidationResult ValidateBitmap(Bitmap bmp, long fileBytes)
    {
        if (bmp.Width <= 0 || bmp.Height <= 0)
        {
            return new NpcGfxPngValidationResult
            {
                Decoded = true,
                Width = bmp.Width,
                Height = bmp.Height,
                FileBytes = fileBytes,
                ContentStatus = NpcGfxPreviewPrepStatus.Failed,
                Reason = "Dimensiones inválidas",
            };
        }

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
            var opaque = 0;
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < argb.Height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < argb.Width; x++)
                {
                    var a = bytes[row + x * 4 + 3];
                    if (a < AlphaThreshold)
                        continue;
                    opaque++;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            var total = argb.Width * argb.Height;
            var visW = maxX >= 0 ? maxX - minX + 1 : 0;
            var visH = maxY >= 0 ? maxY - minY + 1 : 0;
            var visArea = visW * visH;
            var ratio = total > 0 ? (double)opaque / total : 0;

            var status = Classify(opaque, visW, visH, visArea);
            var reason = status switch
            {
                NpcGfxPreviewPrepStatus.Ok => "Contenido visible suficiente",
                NpcGfxPreviewPrepStatus.Failed =>
                    $"Casi vacío (opaque={opaque}, vis={visW}x{visH})",
                NpcGfxPreviewPrepStatus.Review =>
                    $"Ambiguo — revisar (opaque={opaque}, vis={visW}x{visH})",
                _ => null,
            };

            return new NpcGfxPngValidationResult
            {
                Decoded = true,
                Width = argb.Width,
                Height = argb.Height,
                OpaquePixelCount = opaque,
                OpaqueRatio = ratio,
                VisibleWidth = visW,
                VisibleHeight = visH,
                VisibleArea = visArea,
                FileBytes = fileBytes,
                ContentStatus = status,
                Reason = reason,
            };
        }
        finally
        {
            argb.UnlockBits(data);
        }
    }

    /// <summary>
    /// OK: above 9059-calibrated floor. FAILED: at/below 1245 empty band. Else REVIEW.
    /// </summary>
    public static NpcGfxPreviewPrepStatus Classify(
        int opaquePixels,
        int visibleWidth,
        int visibleHeight,
        int visibleArea)
    {
        if (opaquePixels <= FailMaxOpaquePixels
            || visibleWidth <= FailMaxVisibleWidth
            || visibleHeight <= FailMaxVisibleHeight
            || visibleArea <= FailMaxVisibleArea)
            return NpcGfxPreviewPrepStatus.Failed;

        if (opaquePixels >= OkMinOpaquePixels
            && visibleWidth >= OkMinVisibleWidth
            && visibleHeight >= OkMinVisibleHeight
            && visibleArea >= OkMinVisibleArea)
            return NpcGfxPreviewPrepStatus.Ok;

        return NpcGfxPreviewPrepStatus.Review;
    }
}
