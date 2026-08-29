using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// LIB.4.2 — focused artwork SWF → PNG rasterizer for <c>clips/artworks/big/{gfx}.swf</c>.
/// Not a full Flash player: decompresses FWS/CWS, parses DefineShape3 (+ optional DefineBitsLossless2),
/// and paints solid-filled paths with System.Drawing.
/// </summary>
public static class SwfArtworkRasterizer
{
    public const int DefaultSize = 96;

    public static byte[] RasterizeToPng(byte[] swfBytes, int size = DefaultSize)
    {
        using var bmp = RasterizeToBitmap(swfBytes, size);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static Bitmap RasterizeToBitmap(byte[] swfBytes, int size = DefaultSize)
    {
        ArgumentNullException.ThrowIfNull(swfBytes);
        if (size < 16 || size > 512)
            throw new ArgumentOutOfRangeException(nameof(size));

        var body = DecompressBody(swfBytes);
        var shapes = new List<ParsedShape>();
        var bitmaps = new List<Bitmap>();

        foreach (var (code, data) in EnumerateTags(body))
        {
            if (code == 32) // DefineShape3
            {
                try
                {
                    shapes.Add(ParseDefineShape3(data));
                }
                catch
                {
                    // Skip unreadable shapes; other tags may still yield a preview.
                }
            }
            else if (code == 36) // DefineBitsLossless2
            {
                try
                {
                    var bmp = DecodeLossless2(data);
                    if (bmp is not null)
                        bitmaps.Add(bmp);
                }
                catch
                {
                    // ignore
                }
            }
            else if (code is 21 or 35) // DefineBitsJPEG2 / JPEG3
            {
                try
                {
                    var bmp = DecodeJpegTag(data, code == 35);
                    if (bmp is not null)
                        bitmaps.Add(bmp);
                }
                catch
                {
                    // ignore
                }
            }
        }

        var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var drew = false;
            if (shapes.Count > 0)
            {
                foreach (var shape in shapes
                             .OrderByDescending(s => (long)(s.Bounds.XMax - s.Bounds.XMin) * (s.Bounds.YMax - s.Bounds.YMin))
                             .Take(4))
                {
                    if (DrawShape(g, shape, size))
                        drew = true;
                }
            }

            if (!drew && bitmaps.Count > 0)
            {
                var best = bitmaps.OrderByDescending(b => b.Width * b.Height).First();
                var scale = Math.Min((float)size / best.Width, (float)size / best.Height) * 0.92f;
                var w = Math.Max(1, (int)(best.Width * scale));
                var h = Math.Max(1, (int)(best.Height * scale));
                var x = (size - w) / 2;
                var y = (size - h) / 2;
                g.DrawImage(best, new Rectangle(x, y, w, h));
                drew = true;
            }

            if (!drew)
            {
                foreach (var b in bitmaps) b.Dispose();
                canvas.Dispose();
                throw new InvalidOperationException("SWF sin geometría/bitmap rasterizable.");
            }
        }

        foreach (var b in bitmaps) b.Dispose();
        return canvas;
    }

    private static bool DrawShape(Graphics g, ParsedShape shape, int size)
    {
        var b = shape.Bounds;
        var tw = Math.Max(1, b.XMax - b.XMin);
        var th = Math.Max(1, b.YMax - b.YMin);
        var scale = Math.Min(size / (tw / 20f), size / (th / 20f)) * 0.92f;
        var ox = size / 2f - (b.XMin + b.XMax) / 2f / 20f * scale;
        var oy = size / 2f - (b.YMin + b.YMax) / 2f / 20f * scale;
        var drew = false;

        foreach (var path in shape.Paths)
        {
            if (path.Points.Count < 3) continue;
            var fi = path.Fill1 != 0 ? path.Fill1 : path.Fill0;
            if (fi <= 0 || fi > path.Fills.Count) continue;
            var fill = path.Fills[fi - 1];
            if (fill.Kind != FillKind.Solid || fill.Color.A == 0) continue;

            using var gp = new GraphicsPath();
            gp.AddPolygon(path.Points.Select(p => new PointF(
                ox + p.X / 20f * scale,
                oy + p.Y / 20f * scale)).ToArray());
            using var brush = new SolidBrush(Color.FromArgb(fill.Color.A, fill.Color.R, fill.Color.G, fill.Color.B));
            g.FillPath(brush, gp);
            drew = true;
        }

        return drew;
    }

    private static byte[] DecompressBody(byte[] file)
    {
        if (file.Length < 8 || file[1] != (byte)'W' || file[2] != (byte)'S')
            throw new InvalidOperationException("Cabecera SWF inválida.");
        if (file[0] is not ((byte)'F' or (byte)'C'))
            throw new InvalidOperationException("Solo FWS/CWS soportados.");

        if (file[0] == (byte)'F')
            return file[8..];

        using var zs = new ZLibStream(new MemoryStream(file, 8, file.Length - 8), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        zs.CopyTo(ms);
        return ms.ToArray();
    }

    private static IEnumerable<(int Code, byte[] Data)> EnumerateTags(byte[] body)
    {
        var br = new SwfBitReader(body);
        _ = br.ReadRect();
        var pos = br.BytePosition + 4; // frameRate + frameCount
        while (pos < body.Length - 1)
        {
            var codeAndLen = BitConverter.ToUInt16(body, pos);
            pos += 2;
            var code = codeAndLen >> 6;
            var length = codeAndLen & 0x3F;
            if (length == 0x3F)
            {
                length = BitConverter.ToInt32(body, pos);
                pos += 4;
            }

            if (length < 0 || pos + length > body.Length)
                yield break;

            var data = body.AsSpan(pos, length).ToArray();
            pos += length;
            yield return (code, data);
            if (code == 0)
                yield break;
        }
    }

    private static ParsedShape ParseDefineShape3(byte[] payload)
    {
        var charId = BitConverter.ToUInt16(payload, 0);
        var br = new SwfBitReader(payload, 2);
        var bounds = br.ReadRect();
        var fills = ParseFillStyles(br, withAlpha: true);
        var lines = ParseLineStyles(br, withAlpha: true);
        br.Align();
        var numFillBits = br.ReadUb(4);
        var numLineBits = br.ReadUb(4);

        var paths = new List<ShapePath>();
        float x = 0, y = 0;
        var fill0 = 0;
        var fill1 = 0;
        var line = 0;
        var cur = new List<PointF> { new(0, 0) };

        void Flush()
        {
            if (cur.Count >= 2)
            {
                paths.Add(new ShapePath
                {
                    Points = cur.ToList(),
                    Fill0 = fill0,
                    Fill1 = fill1,
                    Line = line,
                    Fills = fills,
                });
            }

            cur = new List<PointF> { new(x, y) };
        }

        while (true)
        {
            if (br.ReadUb(1) == 0)
            {
                var flags = br.ReadUb(5);
                if (flags == 0) break;
                var stateNewStyles = (flags >> 4) & 1;
                var stateLine = (flags >> 3) & 1;
                var stateFill1 = (flags >> 2) & 1;
                var stateFill0 = (flags >> 1) & 1;
                var stateMove = flags & 1;
                if (stateMove != 0)
                {
                    var mb = br.ReadUb(5);
                    x = br.ReadSb(mb);
                    y = br.ReadSb(mb);
                    Flush();
                    cur = new List<PointF> { new(x, y) };
                }

                if (stateFill0 != 0) fill0 = br.ReadUb(numFillBits);
                if (stateFill1 != 0) fill1 = br.ReadUb(numFillBits);
                if (stateLine != 0) line = br.ReadUb(numLineBits);
                if (stateNewStyles != 0)
                {
                    Flush();
                    fills = ParseFillStyles(br, withAlpha: true);
                    lines = ParseLineStyles(br, withAlpha: true);
                    br.Align();
                    numFillBits = br.ReadUb(4);
                    numLineBits = br.ReadUb(4);
                    cur = new List<PointF> { new(x, y) };
                }
            }
            else if (br.ReadUb(1) == 1)
            {
                var nbits = br.ReadUb(4) + 2;
                float dx, dy;
                if (br.ReadUb(1) != 0)
                {
                    dx = br.ReadSb(nbits);
                    dy = br.ReadSb(nbits);
                }
                else if (br.ReadUb(1) != 0)
                {
                    dx = 0;
                    dy = br.ReadSb(nbits);
                }
                else
                {
                    dx = br.ReadSb(nbits);
                    dy = 0;
                }

                x += dx;
                y += dy;
                cur.Add(new PointF(x, y));
            }
            else
            {
                var nbits = br.ReadUb(4) + 2;
                var cx = x + br.ReadSb(nbits);
                var cy = y + br.ReadSb(nbits);
                var ax = cx + br.ReadSb(nbits);
                var ay = cy + br.ReadSb(nbits);
                var ox = x;
                var oy = y;
                foreach (var t in new[] { 0.2f, 0.4f, 0.6f, 0.8f, 1f })
                {
                    var px = (1 - t) * (1 - t) * ox + 2 * (1 - t) * t * cx + t * t * ax;
                    var py = (1 - t) * (1 - t) * oy + 2 * (1 - t) * t * cy + t * t * ay;
                    cur.Add(new PointF(px, py));
                }

                x = ax;
                y = ay;
            }
        }

        Flush();
        _ = charId;
        _ = lines;
        return new ParsedShape { Bounds = bounds, Paths = paths };
    }

    private static List<FillStyle> ParseFillStyles(SwfBitReader br, bool withAlpha)
    {
        var count = (int)br.ReadUi8();
        if (count == 0xFF) count = br.ReadUi16();
        var list = new List<FillStyle>(count);
        for (var i = 0; i < count; i++)
        {
            var ft = br.ReadUi8();
            if (ft == 0x00)
            {
                var c = withAlpha ? br.ReadRgba() : br.ReadRgb();
                list.Add(new FillStyle(FillKind.Solid, c));
            }
            else if (ft is 0x10 or 0x12 or 0x13)
            {
                br.ReadMatrix();
                SkipGradient(br, withAlpha);
                list.Add(new FillStyle(FillKind.Other, default));
            }
            else if (ft is 0x40 or 0x41 or 0x42 or 0x43)
            {
                _ = br.ReadUi16();
                br.ReadMatrix();
                list.Add(new FillStyle(FillKind.Other, default));
            }
            else
            {
                throw new InvalidOperationException($"Fill type 0x{ft:X2} no soportado.");
            }
        }

        return list;
    }

    private static void SkipGradient(SwfBitReader br, bool withAlpha)
    {
        var n = br.ReadUi8() & 0x0F;
        for (var i = 0; i < n; i++)
        {
            _ = br.ReadUi8();
            if (withAlpha) br.ReadRgba();
            else br.ReadRgb();
        }
    }

    private static List<(ushort Width, (byte R, byte G, byte B, byte A) Color)> ParseLineStyles(
        SwfBitReader br, bool withAlpha)
    {
        var count = (int)br.ReadUi8();
        if (count == 0xFF) count = br.ReadUi16();
        var list = new List<(ushort, (byte, byte, byte, byte))>(count);
        for (var i = 0; i < count; i++)
        {
            var w = br.ReadUi16();
            var c = withAlpha ? br.ReadRgba() : br.ReadRgb();
            list.Add((w, c));
        }

        return list;
    }

    private static Bitmap? DecodeLossless2(byte[] data)
    {
        if (data.Length < 8) return null;
        var fmt = data[2];
        var w = BitConverter.ToUInt16(data, 3);
        var h = BitConverter.ToUInt16(data, 5);
        if (w == 0 || h == 0 || fmt != 5) return null;

        using var zs = new ZLibStream(new MemoryStream(data, 7, data.Length - 7), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        zs.CopyTo(ms);
        var raw = ms.ToArray();
        if (raw.Length < w * h * 4) return null;

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, w, h);
        var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bd.Stride;
            var row = new byte[stride];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = (y * w + x) * 4;
                    var a = raw[i];
                    var r = raw[i + 1];
                    var g = raw[i + 2];
                    var b = raw[i + 3];
                    row[x * 4 + 0] = b;
                    row[x * 4 + 1] = g;
                    row[x * 4 + 2] = r;
                    row[x * 4 + 3] = a;
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(bd.Scan0, y * stride), Math.Min(stride, w * 4));
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        return bmp;
    }

    private static Bitmap? DecodeJpegTag(byte[] data, bool jpeg3)
    {
        if (data.Length < 4) return null;
        var offset = 2; // skip character id
        if (jpeg3)
        {
            if (data.Length < 6) return null;
            var alphaOffset = BitConverter.ToUInt32(data, 2);
            offset = 6;
            // JPEG payload then zlib alpha — for preview we only need JPEG.
            var jpegLen = (int)Math.Min(alphaOffset, (uint)(data.Length - offset));
            if (jpegLen <= 0) return null;
            using var ms = new MemoryStream(data, offset, jpegLen);
            return new Bitmap(ms);
        }

        using var ms2 = new MemoryStream(data, offset, data.Length - offset);
        return new Bitmap(ms2);
    }

    private enum FillKind { Solid, Other }

    private readonly record struct FillStyle(FillKind Kind, (byte R, byte G, byte B, byte A) Color);

    private sealed class ShapePath
    {
        public required List<PointF> Points { get; init; }
        public int Fill0 { get; init; }
        public int Fill1 { get; init; }
        public int Line { get; init; }
        public required List<FillStyle> Fills { get; init; }
    }

    private sealed class ParsedShape
    {
        public required (int XMin, int XMax, int YMin, int YMax) Bounds { get; init; }
        public required List<ShapePath> Paths { get; init; }
    }
}
