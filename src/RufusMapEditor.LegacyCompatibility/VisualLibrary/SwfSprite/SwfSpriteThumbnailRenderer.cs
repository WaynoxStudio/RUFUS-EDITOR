using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

/// <summary>
/// ADMIN.UI.4B.2A.3D.2 — composes a static PNG from <c>clips/sprites/{gfx}.swf</c> timelines.
/// </summary>
public static class SwfSpriteThumbnailRenderer
{
    public const int DefaultSize = SwfSpriteLimits.DefaultThumbnailSize;
    public const int CacheVersion = 3;

    public sealed class RenderDiagnostics
    {
        public int? GfxId { get; set; }
        public int SpriteId { get; set; }
        public int FrameIndex { get; set; }
        public string SelectionReason { get; set; } = "";
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public static byte[] RasterizeToPng(byte[] swfBytes, int size = DefaultSize, int? gfxId = null)
    {
        using var bmp = RasterizeToBitmap(swfBytes, size, gfxId, out _);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static Bitmap RasterizeToBitmap(
        byte[] swfBytes,
        int size,
        int? gfxId,
        out RenderDiagnostics? diagnostics)
    {
        diagnostics = null;
        ArgumentNullException.ThrowIfNull(swfBytes);
        size = Math.Clamp(size, SwfSpriteLimits.MinThumbnailSize, SwfSpriteLimits.MaxThumbnailSize);

        SwfMovie movie;
        try
        {
            movie = SwfMovieParser.Parse(swfBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("SWF sprite inválido: " + ex.Message, ex);
        }

        return RasterizeMovie(movie, size, gfxId, null, null, out diagnostics);
    }

    public static Bitmap RasterizeToBitmap(byte[] swfBytes, int size, out RenderDiagnostics? diagnostics) =>
        RasterizeToBitmap(swfBytes, size, null, out diagnostics);

    /// <summary>Debug API — explicit sprite/frame selection.</summary>
    public static Bitmap RasterizeToBitmap(
        byte[] swfBytes,
        int spriteId,
        int frameIndex,
        int size,
        int? gfxId,
        out RenderDiagnostics? diagnostics)
    {
        diagnostics = null;
        ArgumentNullException.ThrowIfNull(swfBytes);
        size = Math.Clamp(size, SwfSpriteLimits.MinThumbnailSize, SwfSpriteLimits.MaxThumbnailSize);
        var movie = SwfMovieParser.Parse(swfBytes);
        return RasterizeMovie(movie, size, gfxId, spriteId, frameIndex, out diagnostics);
    }

    /// <summary>Debug — render explicit sprite/frame from SWF bytes.</summary>
    public static Bitmap RasterizeMovieFrame(byte[] swfBytes, int spriteId, int frameIndex, int size, int? gfxId = null)
    {
        var movie = SwfMovieParser.Parse(swfBytes);
        var composer = new SwfTimelineComposer(movie);
        using var composed = composer.ComposeSprite(spriteId, frameIndex);
        if (composed is null || IsEmpty(composed))
            throw new InvalidOperationException($"Sprite {spriteId} frame {frameIndex} vacío.");
        _ = gfxId;
        return FitThumbnail(composed, Math.Clamp(size, SwfSpriteLimits.MinThumbnailSize, SwfSpriteLimits.MaxThumbnailSize));
    }

    private static Bitmap RasterizeMovie(
        SwfMovie movie,
        int size,
        int? gfxId,
        int? forcedSpriteId,
        int? forcedFrameIndex,
        out RenderDiagnostics? diagnostics)
    {
        IReadOnlyList<SwfSpritePick> candidates = forcedSpriteId is int sid
            ? [new SwfSpritePick(sid, forcedFrameIndex ?? 0, "forced", null)]
            : SwfSpriteSelection.GetThumbnailCandidates(movie);

        var composer = new SwfTimelineComposer(movie);
        SwfSpritePick? chosen = null;
        Bitmap? composed = null;

        foreach (var candidate in candidates)
        {
            var attempt = composer.ComposeSprite(candidate.SpriteId, candidate.FrameIndex);
            if (attempt is null || IsEmpty(attempt))
            {
                attempt?.Dispose();
                continue;
            }

            chosen = candidate;
            composed = attempt;
            break;
        }

        if (chosen is null || composed is null)
        {
            var last = candidates[^1];
            diagnostics = new RenderDiagnostics
            {
                GfxId = gfxId,
                SpriteId = last.SpriteId,
                FrameIndex = last.FrameIndex,
                SelectionReason = last.Reason,
                Success = false,
                Error = "compose empty",
            };
            if (gfxId is int id)
                RufusLog.Info($"SpriteThumb gfx={id} all candidates empty (last={last.Reason})");
            throw new InvalidOperationException("Sprite compuesto vacío.");
        }

        diagnostics = new RenderDiagnostics
        {
            GfxId = gfxId,
            SpriteId = chosen.Value.SpriteId,
            FrameIndex = chosen.Value.FrameIndex,
            SelectionReason = chosen.Value.Reason,
        };

        using (composed)
        {
            var thumb = FitThumbnail(composed, size);
            diagnostics!.Success = true;
            if (gfxId is int gid)
            {
                RufusLog.Info(
                    $"SpriteThumb gfx={gid} linkage={chosen.Value.LinkageName ?? "?"} " +
                    $"sprite={chosen.Value.SpriteId} frame={chosen.Value.FrameIndex} OK ({chosen.Value.Reason})");
            }

            return thumb;
        }
    }

    private static bool IsEmpty(Bitmap bmp)
    {
        for (var y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 8))
        for (var x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 8))
        {
            if (bmp.GetPixel(x, y).A > 8)
                return false;
        }

        return true;
    }

    private static Bitmap FitThumbnail(Bitmap source, int size)
    {
        var margin = size * 0.06f;
        var inner = size - margin * 2;
        var scale = Math.Min(inner / source.Width, inner / source.Height);
        if (scale <= 0) scale = 1;
        var w = Math.Max(1, (int)(source.Width * scale));
        var h = Math.Max(1, (int)(source.Height * scale));
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var x = (size - w) / 2f;
            var y = (size - h) / 2f;
            g.DrawImage(source, new RectangleF(x, y, w, h));
        }

        return dst;
    }
}
