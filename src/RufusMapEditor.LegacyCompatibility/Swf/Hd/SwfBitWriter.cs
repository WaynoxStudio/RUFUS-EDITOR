namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

internal sealed class SwfBitWriter
{
    private readonly MemoryStream _ms = new();
    private int _bitBuf;
    private int _bitCount;

    public void WriteUi8(int v)
    {
        Align();
        _ms.WriteByte((byte)v);
    }

    public void WriteUi16(int v)
    {
        Align();
        _ms.Write(BitConverter.GetBytes((ushort)v));
    }

    public void WriteUb(int nBits, int value)
    {
        for (var i = nBits - 1; i >= 0; i--)
        {
            _bitBuf = (_bitBuf << 1) | ((value >> i) & 1);
            _bitCount++;
            if (_bitCount == 8)
            {
                _ms.WriteByte((byte)_bitBuf);
                _bitBuf = 0;
                _bitCount = 0;
            }
        }
    }

    public void WriteSb(int nBits, int value) => WriteUb(nBits, value);

    public void WriteRect(int xMin, int xMax, int yMin, int yMax)
    {
        var nBits = NeedBits(xMin, xMax, yMin, yMax);
        WriteUb(5, nBits);
        WriteSb(nBits, xMin);
        WriteSb(nBits, xMax);
        WriteSb(nBits, yMin);
        WriteSb(nBits, yMax);
        Align();
    }

    public void WriteIdentityTranslateMatrix(int translateX = 0, int translateY = 0)
    {
        WriteUb(1, 0);
        WriteUb(1, 0);
        var nt = NeedBits(translateX, translateY);
        WriteUb(5, nt);
        WriteSb(nt, translateX);
        WriteSb(nt, translateY);
        Align();
    }

    public void WriteScaleTranslateMatrix(double scaleX, double scaleY, int translateX, int translateY)
    {
        WriteUb(1, 1);
        var sx = (int)Math.Round(scaleX * 65536.0);
        var sy = (int)Math.Round(scaleY * 65536.0);
        var n = Math.Max(NeedBits(sx, sy), 16);
        WriteUb(5, n);
        WriteSb(n, sx);
        WriteSb(n, sy);
        WriteUb(1, 0);
        var nt = NeedBits(translateX, translateY);
        WriteUb(5, nt);
        WriteSb(nt, translateX);
        WriteSb(nt, translateY);
        Align();
    }

    public void Align()
    {
        if (_bitCount == 0)
            return;
        _bitBuf <<= (8 - _bitCount);
        _ms.WriteByte((byte)_bitBuf);
        _bitBuf = 0;
        _bitCount = 0;
    }

    public byte[] ToArray()
    {
        Align();
        return _ms.ToArray();
    }

    internal static int NeedBits(params int[] values)
    {
        var n = 1;
        foreach (var v in values)
        {
            var bits = 1;
            var x = v < 0 ? ~v : v;
            while (x >= (1 << (bits - 1)))
                bits++;
            if (bits > n)
                n = bits;
        }

        return n;
    }
}
