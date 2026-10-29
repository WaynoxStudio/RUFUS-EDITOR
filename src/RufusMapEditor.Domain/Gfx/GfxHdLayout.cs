using System.Text.Json;

namespace RufusMapEditor.Domain.Gfx;

/// <summary>
/// Separates on-disk PNG resolution from the size Flash draws in the game.
/// The SWF must embed <see cref="SourcePixelWidth"/> and scale with a bitmap matrix;
/// it must not bake a resized copy of the PNG.
/// </summary>
public sealed class GfxHdLayout
{
    public const int TwipsPerLogicalPixel = 20;
    public const int Flash8BitmapWarnSize = 2880;
    public const int Flash10BitmapHardLimit = 8191;

    public required int SourcePixelWidth { get; init; }
    public required int SourcePixelHeight { get; init; }
    public required int LogicalWidth { get; init; }
    public required int LogicalHeight { get; init; }
    public int AnchorX { get; init; }
    public int AnchorY { get; init; }
    public int? CropSourceX { get; init; }
    public int? CropSourceY { get; init; }
    public int? CropSourceWidth { get; init; }
    public int? CropSourceHeight { get; init; }

    public double DensityX => LogicalWidth <= 0 ? 1 : SourcePixelWidth / (double)LogicalWidth;
    public double DensityY => LogicalHeight <= 0 ? 1 : SourcePixelHeight / (double)LogicalHeight;

    public int BoundsTwipsWidth => LogicalWidth * TwipsPerLogicalPixel;
    public int BoundsTwipsHeight => LogicalHeight * TwipsPerLogicalPixel;
    public int BoundsXMin => -AnchorX * TwipsPerLogicalPixel;
    public int BoundsYMin => -AnchorY * TwipsPerLogicalPixel;
    public int BoundsXMax => (LogicalWidth - AnchorX) * TwipsPerLogicalPixel;
    public int BoundsYMax => (LogicalHeight - AnchorY) * TwipsPerLogicalPixel;

    /// <summary>Twips per source pixel (bitmap fill matrix scale).</summary>
    public double MatrixScaleX => SourcePixelWidth <= 0 ? TwipsPerLogicalPixel : BoundsTwipsWidth / (double)SourcePixelWidth;
    public double MatrixScaleY => SourcePixelHeight <= 0 ? TwipsPerLogicalPixel : BoundsTwipsHeight / (double)SourcePixelHeight;

    public bool IsHd => DensityX > 1.01 || DensityY > 1.01;

    public static GfxHdLayout Create(
        int sourcePixelWidth,
        int sourcePixelHeight,
        int logicalWidth,
        int logicalHeight,
        int anchorX = 0,
        int anchorY = 0)
    {
        if (sourcePixelWidth <= 0 || sourcePixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourcePixelWidth), "La fuente HD debe tener píxeles.");
        if (logicalWidth <= 0 || logicalHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth), "El tamaño lógico debe ser positivo.");

        return new GfxHdLayout
        {
            SourcePixelWidth = sourcePixelWidth,
            SourcePixelHeight = sourcePixelHeight,
            LogicalWidth = logicalWidth,
            LogicalHeight = logicalHeight,
            AnchorX = anchorX,
            AnchorY = anchorY,
        };
    }

    public static GfxHdLayout FromResource(GfxResource resource, int fallbackWidth, int fallbackHeight)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var sourceW = resource.PixelWidth is int sw && sw > 0 ? sw : fallbackWidth;
        var sourceH = resource.PixelHeight is int sh && sh > 0 ? sh : fallbackHeight;
        var logicalW = resource.PlacementWidth > 0 ? resource.PlacementWidth : sourceW;
        var logicalH = resource.PlacementHeight > 0 ? resource.PlacementHeight : sourceH;
        var ax = resource.Anchor?.X ?? 0;
        var ay = resource.Anchor?.Y ?? 0;
        return Create(sourceW, sourceH, logicalW, logicalH, ax, ay);
    }

    public static (int Width, int Height) PlacementPixels(GfxResource resource, int fallbackWidth, int fallbackHeight)
    {
        var layout = FromResource(resource, fallbackWidth, fallbackHeight);
        return (layout.LogicalWidth, layout.LogicalHeight);
    }

    public static string SidecarPath(string imagePath) =>
        Path.ChangeExtension(imagePath, ".hd.json");

    public static bool TryLoadSidecar(string imagePath, out GfxHdSidecar sidecar)
    {
        sidecar = default!;
        var path = SidecarPath(imagePath);
        if (!File.Exists(path))
            return false;
        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<GfxHdSidecar>(json, JsonOptions());
            if (parsed is null || parsed.LogicalWidth <= 0 || parsed.LogicalHeight <= 0)
                return false;
            sidecar = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveSidecar(string imagePath, GfxHdLayout layout)
    {
        var dto = new GfxHdSidecar
        {
            LogicalWidth = layout.LogicalWidth,
            LogicalHeight = layout.LogicalHeight,
            SourcePixelWidth = layout.SourcePixelWidth,
            SourcePixelHeight = layout.SourcePixelHeight,
            AnchorX = layout.AnchorX,
            AnchorY = layout.AnchorY,
            CropSourceX = layout.CropSourceX,
            CropSourceY = layout.CropSourceY,
            CropSourceWidth = layout.CropSourceWidth,
            CropSourceHeight = layout.CropSourceHeight,
        };
        File.WriteAllText(SidecarPath(imagePath), JsonSerializer.Serialize(dto, JsonOptions()));
    }

    public GfxHdFlashLimit CheckFlashLimit()
    {
        var max = Math.Max(SourcePixelWidth, SourcePixelHeight);
        if (max > Flash10BitmapHardLimit)
            return GfxHdFlashLimit.HardExceeds;
        if (max > Flash8BitmapWarnSize)
            return GfxHdFlashLimit.WarnExceedsFlash8;
        return GfxHdFlashLimit.Ok;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
}

public sealed class GfxHdSidecar
{
    public int LogicalWidth { get; set; }
    public int LogicalHeight { get; set; }
    public int SourcePixelWidth { get; set; }
    public int SourcePixelHeight { get; set; }
    public int AnchorX { get; set; }
    public int AnchorY { get; set; }
    public int? CropSourceX { get; set; }
    public int? CropSourceY { get; set; }
    public int? CropSourceWidth { get; set; }
    public int? CropSourceHeight { get; set; }
}

public enum GfxHdFlashLimit
{
    Ok = 0,
    WarnExceedsFlash8 = 1,
    HardExceeds = 2,
}

public static class GfxHdSidecarBinder
{
    public static void Apply(ref GfxResource resource)
    {
        if (!GfxHdLayout.TryLoadSidecar(resource.FilePath, out var sidecar))
            return;
        resource = new GfxResource
        {
            Id = resource.Id,
            Category = resource.Category,
            FilePath = resource.FilePath,
            Folder = resource.Folder,
            Extension = resource.Extension,
            Anchor = resource.Anchor,
            AnchorAmbiguous = resource.AnchorAmbiguous,
            PixelWidth = resource.PixelWidth,
            PixelHeight = resource.PixelHeight,
            LogicalWidth = sidecar.LogicalWidth,
            LogicalHeight = sidecar.LogicalHeight,
        };
    }
}
