using System.Drawing;
using System.Drawing.Imaging;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

/// <summary>
/// Single import path for backgrounds, objects and animations: keep the source PNG,
/// write an HD sidecar, and emit a SWF that scales with a bitmap matrix.
/// </summary>
public static class GfxHdImportService
{
    public static void CopySourceWithoutResize(string sourcePngPath, string destinationPngPath, GfxHdLayout layout)
    {
        using var src = new Bitmap(sourcePngPath);
        SwfHdBitmapEncoder.EnsureMatchesLayout(src, layout);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPngPath)!);
        src.Save(destinationPngPath, ImageFormat.Png);
        GfxHdLayout.SaveSidecar(destinationPngPath, layout);
    }

    public static (byte[] Swf, SwfHdPublishCheck Check) EncodeStandaloneSwf(
        Bitmap source,
        GfxHdLayout layout,
        string exportName)
    {
        var tags = SwfHdSymbolBuilder.BuildStatic(source, layout, 1, 2, 3, exportName);
        var movie = SwfHdSymbolBuilder.WrapMovie(
            tags.Concat(),
            layout.BoundsTwipsWidth,
            layout.BoundsTwipsHeight);
        var check = SwfHdPublishValidator.EvaluateEncodedMovie(source, layout, movie);
        return (movie, check);
    }
}
