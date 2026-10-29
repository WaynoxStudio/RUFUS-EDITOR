using System.IO.Compression;

namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

public sealed class SwfHdReadbackReport
{
    public int BitmapWidth { get; init; }
    public int BitmapHeight { get; init; }
    public int BoundsTwipsWidth { get; init; }
    public int BoundsTwipsHeight { get; init; }
    public double MatrixScaleX { get; init; }
    public double MatrixScaleY { get; init; }
    public int BitmapTagCount { get; init; }
}

public static class SwfHdReadback
{
    public static SwfHdReadbackReport Parse(byte[] swf)
    {
        var body = Decompress(swf);
        var i = 0;
        var nBits = (body[0] >> 3) & 0x1F;
        i += (5 + nBits * 4 + 7) / 8;
        i += 4;

        var bmpW = 0;
        var bmpH = 0;
        var bmpCount = 0;
        var boundsW = 0;
        var boundsH = 0;
        var scaleX = 0d;
        var scaleY = 0d;

        while (i + 2 <= body.Length)
        {
            var rec = BitConverter.ToUInt16(body, i);
            i += 2;
            var code = rec >> 6;
            var len = rec & 0x3F;
            if (len == 0x3F)
            {
                len = BitConverter.ToInt32(body, i);
                i += 4;
            }

            if (len < 0 || i + len > body.Length)
                break;
            var data = body.AsSpan(i, len);
            i += len;
            if (code == 0)
                break;
            if (code == 36 && data.Length >= 7)
            {
                bmpCount++;
                bmpW = BitConverter.ToUInt16(data[3..]);
                bmpH = BitConverter.ToUInt16(data[5..]);
            }
            else if (code is 32 or 83 && data.Length > 8)
            {
                ParseShape(data.ToArray(), out boundsW, out boundsH, out scaleX, out scaleY);
            }
        }

        return new SwfHdReadbackReport
        {
            BitmapWidth = bmpW,
            BitmapHeight = bmpH,
            BoundsTwipsWidth = boundsW,
            BoundsTwipsHeight = boundsH,
            MatrixScaleX = scaleX,
            MatrixScaleY = scaleY,
            BitmapTagCount = bmpCount,
        };
    }

    private static void ParseShape(byte[] payload, out int boundsW, out int boundsH, out double scaleX, out double scaleY)
    {
        boundsW = 0;
        boundsH = 0;
        scaleX = 0;
        scaleY = 0;
        var br = new VisualLibrary.SwfBitReader(payload, 2);
        var (xMin, xMax, yMin, yMax) = br.ReadRect();
        boundsW = xMax - xMin;
        boundsH = yMax - yMin;
        if (payload.Length > 2 && (payload[0] != 0 || payload[1] != 0))
        {
            // Shape4 has a second edge RECT after bounds.
        }

        var fillCount = br.ReadUi8();
        if (fillCount < 1)
            return;
        var fillType = br.ReadUi8();
        if (fillType is not (0x40 or 0x41))
            return;
        _ = br.ReadUi16();
        br.Align();
        var hasScale = br.ReadUb(1) != 0;
        if (hasScale)
        {
            var n = br.ReadUb(5);
            scaleX = br.ReadSb(n) / 65536.0;
            scaleY = br.ReadSb(n) / 65536.0;
        }
    }

    private static byte[] Decompress(byte[] swf)
    {
        if (swf.Length < 8)
            throw new InvalidOperationException("SWF demasiado corto.");
        if (swf[0] == (byte)'F')
            return swf.AsSpan(8).ToArray();
        using var ms = new MemoryStream(swf, 8, swf.Length - 8);
        using var zs = new ZLibStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        zs.CopyTo(outMs);
        return outMs.ToArray();
    }
}
