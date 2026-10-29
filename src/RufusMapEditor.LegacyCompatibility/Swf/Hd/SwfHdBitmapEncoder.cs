using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

/// <summary>DefineBitsLossless2 (tag 36) from the original PNG pixels — never a resized preview.</summary>
public static class SwfHdBitmapEncoder
{
    public static byte[] EncodeDefineBitsLossless2(ushort bitmapId, Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width <= 0 || source.Height <= 0)
            throw new ArgumentException("Bitmap vacío.", nameof(source));

        var w = source.Width;
        var h = source.Height;
        var argb = new byte[w * h * 4];
        var rect = new Rectangle(0, 0, w, h);
        var bd = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bd.Stride;
            var row = new byte[Math.Abs(stride)];
            for (var y = 0; y < h; y++)
            {
                Marshal.Copy(IntPtr.Add(bd.Scan0, y * stride), row, 0, row.Length);
                for (var x = 0; x < w; x++)
                {
                    var bi = x * 4;
                    var oi = (y * w + x) * 4;
                    var b = row[bi];
                    var g = row[bi + 1];
                    var r = row[bi + 2];
                    var a = row[bi + 3];
                    argb[oi] = a;
                    argb[oi + 1] = (byte)((r * a) / 255);
                    argb[oi + 2] = (byte)((g * a) / 255);
                    argb[oi + 3] = (byte)((b * a) / 255);
                }
            }
        }
        finally
        {
            source.UnlockBits(bd);
        }

        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(bitmapId));
        ms.WriteByte(5);
        ms.Write(BitConverter.GetBytes((ushort)w));
        ms.Write(BitConverter.GetBytes((ushort)h));
        using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zs.Write(argb);
        return SwfHdTagWriter.Tag(36, ms.ToArray());
    }

    public static void EnsureMatchesLayout(Bitmap source, GfxHdLayout layout)
    {
        if (source.Width != layout.SourcePixelWidth || source.Height != layout.SourcePixelHeight)
        {
            throw new InvalidOperationException(
                $"La fuente es {source.Width}×{source.Height} pero el layout HD espera {layout.SourcePixelWidth}×{layout.SourcePixelHeight}. " +
                "No se redimensiona en silencio.");
        }
    }
}
