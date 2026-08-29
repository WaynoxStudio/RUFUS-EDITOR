using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal sealed class SwfBitmapDefinition
{
    public required int CharacterId { get; init; }
    public required Bitmap Bitmap { get; init; }
}

internal static class SwfBitmapParser
{
    public static SwfBitmapDefinition? Parse(int tagCode, byte[] data)
    {
        return tagCode switch
        {
            20 => ParseDefineBitsLossless(data, alpha: false),
            36 => ParseDefineBitsLossless(data, alpha: true),
            21 or 35 => ParseJpeg(data, tagCode == 35),
            _ => null,
        };
    }

    private static SwfBitmapDefinition? ParseDefineBitsLossless(byte[] data, bool alpha)
    {
        if (data.Length < 7) return null;
        var charId = BitConverter.ToUInt16(data, 0);
        var fmt = data[2];
        var w = BitConverter.ToUInt16(data, 3);
        var h = BitConverter.ToUInt16(data, 5);
        if (w == 0 || h == 0) return null;

        var offset = 7;
        byte[] raw;
        if (fmt is 3 or 4 or 5)
        {
            using var zs = new ZLibStream(new MemoryStream(data, offset, data.Length - offset), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            zs.CopyTo(ms);
            raw = ms.ToArray();
        }
        else
        {
            return null;
        }

        var bpp = fmt switch
        {
            3 => 3,
            4 => 4,
            5 => 4,
            _ => 0,
        };
        if (bpp == 0) return null;

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, w, h);
        var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bd.Stride;
            var row = new byte[stride];
            var pos = 0;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    byte a, r, g, b;
                    if (fmt == 3)
                    {
                        var paletteSize = raw[pos++] + 1;
                        var idx = raw[pos++];
                        var pi = idx * 3;
                        if (pi + 2 >= raw.Length) break;
                        r = raw[pi];
                        g = raw[pi + 1];
                        b = raw[pi + 2];
                        a = 255;
                        _ = paletteSize;
                    }
                    else
                    {
                        if (pos + 3 >= raw.Length) break;
                        if (alpha)
                        {
                            a = raw[pos++];
                            r = raw[pos++];
                            g = raw[pos++];
                            b = raw[pos++];
                        }
                        else
                        {
                            r = raw[pos++];
                            g = raw[pos++];
                            b = raw[pos++];
                            a = 255;
                        }
                    }

                    row[x * 4 + 0] = b;
                    row[x * 4 + 1] = g;
                    row[x * 4 + 2] = r;
                    row[x * 4 + 3] = a;
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(bd.Scan0, y * stride),
                    Math.Min(stride, w * 4));
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        return new SwfBitmapDefinition { CharacterId = charId, Bitmap = bmp };
    }

    private static SwfBitmapDefinition? ParseJpeg(byte[] data, bool jpeg3)
    {
        if (data.Length < 4) return null;
        var charId = BitConverter.ToUInt16(data, 0);
        var offset = 2;
        if (jpeg3)
        {
            if (data.Length < 6) return null;
            var alphaOffset = BitConverter.ToUInt32(data, 2);
            offset = 6;
            var jpegLen = (int)Math.Min(alphaOffset, (uint)(data.Length - offset));
            if (jpegLen <= 0) return null;
            using var ms = new MemoryStream(data, offset, jpegLen);
            return new SwfBitmapDefinition { CharacterId = charId, Bitmap = new Bitmap(ms) };
        }

        using var ms2 = new MemoryStream(data, offset, data.Length - offset);
        return new SwfBitmapDefinition { CharacterId = charId, Bitmap = new Bitmap(ms2) };
    }
}
