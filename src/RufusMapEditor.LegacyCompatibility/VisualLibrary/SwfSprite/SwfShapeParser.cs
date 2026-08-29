using System.Drawing;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal enum SwfFillKind { None, Solid, Other }

internal readonly record struct SwfFillStyle(SwfFillKind Kind, Color Color);

internal sealed class SwfShapePath
{
    public required List<PointF> Points { get; init; }
    public int Fill0 { get; init; }
    public int Fill1 { get; init; }
    public required IReadOnlyList<SwfFillStyle> Fills { get; init; }
}

internal sealed class SwfShapeDefinition
{
    public required int CharacterId { get; init; }
    public required (int XMin, int XMax, int YMin, int YMax) Bounds { get; init; }
    public required List<SwfShapePath> Paths { get; init; }
}

internal static class SwfShapeParser
{
    public static SwfShapeDefinition Parse(int shapeCode, byte[] payload)
    {
        var withAlpha = shapeCode is 32 or 33 or 83;
        var charId = BitConverter.ToUInt16(payload, 0);
        var br = new SwfBitReader(payload, 2);
        var bounds = br.ReadRect();
        var fills = ParseFillStyles(br, withAlpha);
        if (shapeCode is 22 or 32 or 33 or 83)
            _ = ParseLineStyles(br, withAlpha);
        br.Align();
        var numFillBits = br.ReadUb(4);
        var numLineBits = br.ReadUb(4);
        var paths = ParseShapeRecords(br, fills, numFillBits, numLineBits);
        return new SwfShapeDefinition
        {
            CharacterId = charId,
            Bounds = bounds,
            Paths = paths,
        };
    }

    private static List<SwfShapePath> ParseShapeRecords(
        SwfBitReader br,
        List<SwfFillStyle> fills,
        int numFillBits,
        int numLineBits)
    {
        var paths = new List<SwfShapePath>();
        float x = 0, y = 0;
        var fill0 = 0;
        var fill1 = 0;
        var cur = new List<PointF> { new(0, 0) };

        void Flush()
        {
            if (cur.Count >= 2)
            {
                paths.Add(new SwfShapePath
                {
                    Points = cur.ToList(),
                    Fill0 = fill0,
                    Fill1 = fill1,
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
                    cur = new List<PointF> { new(x / 20f, y / 20f) };
                }

                if (stateFill0 != 0) fill0 = br.ReadUb(numFillBits);
                if (stateFill1 != 0) fill1 = br.ReadUb(numFillBits);
                if (stateLine != 0) _ = br.ReadUb(numLineBits);
                if (stateNewStyles != 0)
                {
                    Flush();
                    fills = ParseFillStyles(br, true);
                    _ = ParseLineStyles(br, true);
                    br.Align();
                    numFillBits = br.ReadUb(4);
                    numLineBits = br.ReadUb(4);
                    cur = new List<PointF> { new(x / 20f, y / 20f) };
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
                cur.Add(new PointF(x / 20f, y / 20f));
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
                    cur.Add(new PointF(px / 20f, py / 20f));
                }

                x = ax;
                y = ay;
            }
        }

        Flush();
        return paths;
    }

    private static List<SwfFillStyle> ParseFillStyles(SwfBitReader br, bool withAlpha)
    {
        var count = (int)br.ReadUi8();
        if (count == 0xFF) count = br.ReadUi16();
        var list = new List<SwfFillStyle>(count);
        for (var i = 0; i < count; i++)
        {
            var ft = br.ReadUi8();
            if (ft == 0x00)
            {
                var c = withAlpha ? br.ReadRgba() : br.ReadRgb();
                list.Add(new SwfFillStyle(SwfFillKind.Solid,
                    Color.FromArgb(c.A, c.R, c.G, c.B)));
            }
            else if (ft is 0x10 or 0x12 or 0x13)
            {
                br.ReadMatrix();
                SkipGradient(br, withAlpha);
                list.Add(new SwfFillStyle(SwfFillKind.Other, default));
            }
            else if (ft is 0x40 or 0x41 or 0x42 or 0x43)
            {
                _ = br.ReadUi16();
                br.ReadMatrix();
                list.Add(new SwfFillStyle(SwfFillKind.Other, default));
            }
            else
            {
                list.Add(new SwfFillStyle(SwfFillKind.Other, default));
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

    private static List<(ushort Width, Color Color)> ParseLineStyles(SwfBitReader br, bool withAlpha)
    {
        var count = (int)br.ReadUi8();
        if (count == 0xFF) count = br.ReadUi16();
        var list = new List<(ushort, Color)>(count);
        for (var i = 0; i < count; i++)
        {
            var w = br.ReadUi16();
            var c = withAlpha ? br.ReadRgba() : br.ReadRgb();
            list.Add((w, Color.FromArgb(c.A, c.R, c.G, c.B)));
        }

        return list;
    }
}
