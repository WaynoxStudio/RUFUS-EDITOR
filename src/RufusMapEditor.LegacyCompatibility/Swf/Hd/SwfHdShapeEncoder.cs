using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

/// <summary>DefineShape3 with a clipped bitmap fill scaled to logical bounds.</summary>
public static class SwfHdShapeEncoder
{
    public static byte[] EncodeBitmapFillShape(ushort shapeId, ushort bitmapId, GfxHdLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var xMin = layout.BoundsXMin;
        var xMax = layout.BoundsXMax;
        var yMin = layout.BoundsYMin;
        var yMax = layout.BoundsYMax;
        var tw = xMax - xMin;
        var th = yMax - yMin;

        var bw = new SwfBitWriter();
        bw.WriteUi16(shapeId);
        bw.WriteRect(xMin, xMax, yMin, yMax);
        bw.WriteUi8(1);
        bw.WriteUi8(0x41);
        bw.WriteUi16(bitmapId);
        bw.WriteScaleTranslateMatrix(layout.MatrixScaleX, layout.MatrixScaleY, xMin, yMin);
        bw.WriteUi8(0);
        bw.WriteUb(4, 1);
        bw.WriteUb(4, 0);
        bw.WriteUb(1, 0);
        // STYLECHANGERECORD flags are NewStyles, LineStyle, FillStyle1,
        // FillStyle0, MoveTo (MSB to LSB).  We move to the anchored origin
        // and select fill style 1; no new style table follows this record.
        // 0b10001 incorrectly advertised StateNewStyles and made Flash read
        // the following edge bits as another style table, corrupting the SWF.
        bw.WriteUb(5, 0b00011);
        var moveBits = SwfBitWriter.NeedBits(xMin, yMin);
        bw.WriteUb(5, moveBits);
        bw.WriteSb(moveBits, xMin);
        bw.WriteSb(moveBits, yMin);
        bw.WriteUb(1, 1);
        WriteBox(bw, tw, th);
        bw.WriteUb(1, 0);
        bw.WriteUb(5, 0);
        bw.Align();
        return SwfHdTagWriter.Tag(32, bw.ToArray());
    }

    private static void WriteBox(SwfBitWriter bw, int tw, int th)
    {
        WriteLine(bw, tw, 0);
        WriteLine(bw, 0, th);
        WriteLine(bw, -tw, 0);
        WriteLine(bw, 0, -th);
    }

    private static void WriteLine(SwfBitWriter bw, int dx, int dy)
    {
        bw.WriteUb(1, 1);
        bw.WriteUb(1, 1);
        var nBits = Math.Max(2, SwfBitWriter.NeedBits(dx, dy));
        bw.WriteUb(4, nBits - 2);
        if (dx != 0 && dy != 0)
        {
            bw.WriteUb(1, 1);
            bw.WriteSb(nBits, dx);
            bw.WriteSb(nBits, dy);
        }
        else if (dx != 0)
        {
            bw.WriteUb(1, 0);
            bw.WriteUb(1, 0);
            bw.WriteSb(nBits, dx);
        }
        else
        {
            bw.WriteUb(1, 0);
            bw.WriteUb(1, 1);
            bw.WriteSb(nBits, dy);
        }
    }
}
