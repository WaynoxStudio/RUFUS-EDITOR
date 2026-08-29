using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RufusMapEditor.App.Services;

internal static class BitmapConversion
{
    public static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            // Always 96 DPI so MapImage DIPs == pixels and stay aligned with OverlayCanvas
            // placement (GfxOverlayCache also freezes bitmaps at 96 DPI). Stretch/None +
            // system DPI metadata otherwise shifts final GFX vs preview diamonds.
            var source = BitmapSource.Create(
                bitmap.Width, bitmap.Height,
                96, 96,
                PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * bitmap.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
