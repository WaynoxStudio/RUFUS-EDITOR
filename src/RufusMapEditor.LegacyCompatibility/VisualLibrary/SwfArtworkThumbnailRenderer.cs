using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// ADMIN.UI.4B.2A.3F.1 — timeline-aware artwork thumbnail renderer for
/// <c>clips/artworks/big/{gfx}.swf</c> (Gestor de Looks gallery pipeline).
/// Entry point: root movie timeline frame 0 — not ExportAssets/staticR.
/// </summary>
public static class SwfArtworkThumbnailRenderer
{
    public const int CacheVersion = 1;
    public const int DefaultThumbnailSize = 128;
    public const int MinThumbnailSize = 48;
    public const int MaxThumbnailSize = 512;

    public enum ArtworkRenderStrategy
    {
        RootTimeline,
        InternalSprite,
        LegacyShapeScraper,
        Failed,
    }

    public sealed class RenderDiagnostics
    {
        public int? GfxId { get; set; }
        public ArtworkRenderStrategy Strategy { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int ShapesDrawn { get; set; }
        public int NestedSprites { get; set; }
        public int BitmapsDrawn { get; set; }
    }

    public static byte[] RasterizeToPng(byte[] swfBytes, int size = DefaultThumbnailSize, int? gfxId = null) =>
        RasterizeToPng(swfBytes, size, gfxId, out _);

    public static byte[] RasterizeToPng(
        byte[] swfBytes,
        int size,
        int? gfxId,
        out RenderDiagnostics diagnostics)
    {
        using var bmp = RasterizeToBitmap(swfBytes, size, gfxId, out diagnostics);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static Bitmap RasterizeToBitmap(byte[] swfBytes, int size = DefaultThumbnailSize, int? gfxId = null) =>
        RasterizeToBitmap(swfBytes, size, gfxId, out _);

    public static Bitmap RasterizeToBitmap(
        byte[] swfBytes,
        int size,
        int? gfxId,
        out RenderDiagnostics diagnostics)
    {
        diagnostics = new RenderDiagnostics { GfxId = gfxId };
        ArgumentNullException.ThrowIfNull(swfBytes);
        size = Math.Clamp(size, MinThumbnailSize, MaxThumbnailSize);

        SwfMovie movie;
        try
        {
            movie = SwfMovieParser.Parse(swfBytes);
        }
        catch (Exception ex)
        {
            diagnostics.Strategy = ArtworkRenderStrategy.Failed;
            diagnostics.Error = ex.Message;
            throw new InvalidOperationException("SWF artwork inválido: " + ex.Message, ex);
        }

        var composer = new SwfTimelineComposer(movie);
        var analysis = composer.AnalyzeRoot(0);
        diagnostics.ShapesDrawn = analysis.ShapesDrawn;
        diagnostics.NestedSprites = analysis.NestedSprites;
        diagnostics.BitmapsDrawn = analysis.BitmapsDrawn;

        using var composed = composer.ComposeRoot(0);
        if (composed is not null && !IsEmpty(composed))
        {
            return FinishArtwork(composed, size, gfxId, diagnostics, ArtworkRenderStrategy.RootTimeline);
        }

        var bestInternal = movie.Sprites.Values
            .OrderByDescending(s => s.PayloadBytes)
            .FirstOrDefault(s => s.PayloadBytes > 32);
        if (bestInternal is not null)
        {
            using var internalBmp = composer.ComposeSprite(bestInternal.CharacterId, 0);
            if (internalBmp is not null && !IsEmpty(internalBmp))
            {
                if (gfxId is int gid)
                    RufusLog.Info($"ArtworkThumb gfx={gid} fallback=internalSprite id={bestInternal.CharacterId}");
                return FinishArtwork(
                    internalBmp,
                    size,
                    gfxId,
                    diagnostics,
                    ArtworkRenderStrategy.InternalSprite);
            }
        }

        try
        {
            var legacySize = Math.Max(size, 256);
            using var legacy = SwfArtworkRasterizer.RasterizeToBitmap(swfBytes, legacySize);
            if (!IsEmpty(legacy))
            {
                if (gfxId is int gid)
                    RufusLog.Info($"ArtworkThumb gfx={gid} fallback=legacy shapes OK");
                return FinishArtwork(
                    legacy,
                    size,
                    gfxId,
                    diagnostics,
                    ArtworkRenderStrategy.LegacyShapeScraper);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Error = ex.Message;
        }

        diagnostics.Strategy = SwfArtworkThumbnailRenderer.ArtworkRenderStrategy.Failed;
        diagnostics.Success = false;
        throw new InvalidOperationException("Artwork compuesto vacío.");
    }

    private static Bitmap FinishArtwork(
        Bitmap composed,
        int size,
        int? gfxId,
        RenderDiagnostics diagnostics,
        SwfArtworkThumbnailRenderer.        ArtworkRenderStrategy strategy)
    {
        diagnostics.Strategy = strategy;
        diagnostics.Success = true;
        _ = gfxId;
        return FitThumbnail(composed, size);
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
