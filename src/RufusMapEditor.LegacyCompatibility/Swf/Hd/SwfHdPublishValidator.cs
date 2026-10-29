using System.Drawing;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

public sealed class SwfHdPublishCheck
{
    public required bool CanPublish { get; init; }
    public required bool WouldPhysicallyDownsample { get; init; }
    public required bool ThumbnailWouldReplaceSource { get; init; }
    public required GfxHdFlashLimit FlashLimit { get; init; }
    public required int SourcePixelWidth { get; init; }
    public required int SourcePixelHeight { get; init; }
    public required int LogicalWidth { get; init; }
    public required int LogicalHeight { get; init; }
    public required double DensityX { get; init; }
    public required double DensityY { get; init; }
    public int EncodedBitmapWidth { get; init; }
    public int EncodedBitmapHeight { get; init; }
    public int EncodedBoundsTwipsWidth { get; init; }
    public int EncodedBoundsTwipsHeight { get; init; }
    public double EncodedMatrixScaleX { get; init; }
    public double EncodedMatrixScaleY { get; init; }
    public required string Summary { get; init; }
}

public static class SwfHdPublishValidator
{
    public static SwfHdPublishCheck Evaluate(
        GfxHdLayout layout,
        int encodedBitmapWidth,
        int encodedBitmapHeight,
        int? thumbnailWidth = null,
        int? thumbnailHeight = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var down = encodedBitmapWidth < layout.SourcePixelWidth || encodedBitmapHeight < layout.SourcePixelHeight;
        var thumbSwap = thumbnailWidth is int tw && tw > 0 && thumbnailHeight is int th && th > 0
            && tw == layout.LogicalWidth && th == layout.LogicalHeight
            && (layout.SourcePixelWidth > tw || layout.SourcePixelHeight > th)
            && encodedBitmapWidth == tw && encodedBitmapHeight == th;
        var flash = layout.CheckFlashLimit();
        var can = !down && flash != GfxHdFlashLimit.HardExceeds;
        var summary =
            $"Fuente {layout.SourcePixelWidth}×{layout.SourcePixelHeight} · " +
            $"lógico {layout.LogicalWidth}×{layout.LogicalHeight} · " +
            $"densidad {layout.DensityX:0.##}×{layout.DensityY:0.##} · " +
            $"SWF {encodedBitmapWidth}×{encodedBitmapHeight}";
        if (down)
            summary += " · BLOQUEO: el SWF reduciría la fuente.";
        if (thumbSwap)
            summary += " · BLOQUEO: una miniatura sustituiría a la fuente HD.";
        if (flash == GfxHdFlashLimit.HardExceeds)
            summary += $" · BLOQUEO: Flash no admite bitmaps > {GfxHdLayout.Flash10BitmapHardLimit}px.";
        else if (flash == GfxHdFlashLimit.WarnExceedsFlash8)
            summary += $" · AVISO: supera {GfxHdLayout.Flash8BitmapWarnSize}px (límite Flash 8).";

        return new SwfHdPublishCheck
        {
            CanPublish = can && !thumbSwap,
            WouldPhysicallyDownsample = down,
            ThumbnailWouldReplaceSource = thumbSwap,
            FlashLimit = flash,
            SourcePixelWidth = layout.SourcePixelWidth,
            SourcePixelHeight = layout.SourcePixelHeight,
            LogicalWidth = layout.LogicalWidth,
            LogicalHeight = layout.LogicalHeight,
            DensityX = layout.DensityX,
            DensityY = layout.DensityY,
            EncodedBitmapWidth = encodedBitmapWidth,
            EncodedBitmapHeight = encodedBitmapHeight,
            Summary = summary,
        };
    }

    public static SwfHdPublishCheck EvaluateEncodedMovie(Bitmap source, GfxHdLayout layout, byte[] swf)
    {
        SwfHdBitmapEncoder.EnsureMatchesLayout(source, layout);
        var read = SwfHdReadback.Parse(swf);
        var check = Evaluate(layout, read.BitmapWidth, read.BitmapHeight, layout.LogicalWidth, layout.LogicalHeight);
        return new SwfHdPublishCheck
        {
            CanPublish = check.CanPublish
                && read.BitmapWidth == layout.SourcePixelWidth
                && read.BitmapHeight == layout.SourcePixelHeight
                && read.BoundsTwipsWidth == layout.BoundsTwipsWidth
                && read.BoundsTwipsHeight == layout.BoundsTwipsHeight,
            WouldPhysicallyDownsample = check.WouldPhysicallyDownsample,
            ThumbnailWouldReplaceSource = check.ThumbnailWouldReplaceSource,
            FlashLimit = check.FlashLimit,
            SourcePixelWidth = check.SourcePixelWidth,
            SourcePixelHeight = check.SourcePixelHeight,
            LogicalWidth = check.LogicalWidth,
            LogicalHeight = check.LogicalHeight,
            DensityX = check.DensityX,
            DensityY = check.DensityY,
            EncodedBitmapWidth = read.BitmapWidth,
            EncodedBitmapHeight = read.BitmapHeight,
            EncodedBoundsTwipsWidth = read.BoundsTwipsWidth,
            EncodedBoundsTwipsHeight = read.BoundsTwipsHeight,
            EncodedMatrixScaleX = read.MatrixScaleX,
            EncodedMatrixScaleY = read.MatrixScaleY,
            Summary = check.Summary +
                $" · bounds {read.BoundsTwipsWidth}×{read.BoundsTwipsHeight} twips" +
                $" · matriz {read.MatrixScaleX:0.###}×{read.MatrixScaleY:0.###}",
        };
    }

    public static SwfHdPublishCheck InspectLibraryFile(string pngPath, int logicalWidth, int logicalHeight)
    {
        using var bmp = new Bitmap(pngPath);
        var layout = GfxHdLayout.Create(bmp.Width, bmp.Height, logicalWidth, logicalHeight);
        return Evaluate(layout, bmp.Width, bmp.Height);
    }
}
