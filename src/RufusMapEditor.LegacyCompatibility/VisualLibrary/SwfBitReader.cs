namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.4.2 — bit reader for SWF shape / matrix fields.</summary>
internal sealed class SwfBitReader
{
    private readonly byte[] _data;
    private int _bytePos;
    private int _bitPos;

    public SwfBitReader(byte[] data, int offset = 0)
    {
        _data = data;
        _bytePos = offset;
    }

    public int BytePosition => _bytePos;
    public int Remaining => _data.Length - _bytePos;

    public void Align()
    {
        if (_bitPos == 0) return;
        _bytePos++;
        _bitPos = 0;
    }

    public int ReadUb(int n)
    {
        var v = 0;
        for (var i = 0; i < n; i++)
        {
            if (_bytePos >= _data.Length)
                throw new EndOfStreamException("SWF bit stream exhausted.");
            var bit = (_data[_bytePos] >> (7 - _bitPos)) & 1;
            v = (v << 1) | bit;
            _bitPos++;
            if (_bitPos == 8)
            {
                _bitPos = 0;
                _bytePos++;
            }
        }

        return v;
    }

    public int ReadSb(int n)
    {
        var v = ReadUb(n);
        if (n > 0 && (v & (1 << (n - 1))) != 0)
            v -= 1 << n;
        return v;
    }

    public float ReadFb(int n) => ReadSb(n) / 65536f;

    public string ReadString()
    {
        Align();
        var start = _bytePos;
        while (_bytePos < _data.Length && _data[_bytePos] != 0)
            _bytePos++;
        var s = System.Text.Encoding.UTF8.GetString(_data, start, _bytePos - start);
        if (_bytePos < _data.Length)
            _bytePos++;
        return s;
    }

    public (int XMin, int XMax, int YMin, int YMax) ReadRect()
    {
        var nbits = ReadUb(5);
        var xMin = ReadSb(nbits);
        var xMax = ReadSb(nbits);
        var yMin = ReadSb(nbits);
        var yMax = ReadSb(nbits);
        Align();
        return (xMin, xMax, yMin, yMax);
    }

    public void ReadMatrix()
    {
        Align();
        if (ReadUb(1) != 0)
        {
            var n = ReadUb(5);
            _ = ReadFb(n);
            _ = ReadFb(n);
        }

        if (ReadUb(1) != 0)
        {
            var n = ReadUb(5);
            _ = ReadFb(n);
            _ = ReadFb(n);
        }

        var tn = ReadUb(5);
        _ = ReadSb(tn);
        _ = ReadSb(tn);
        Align();
    }

    public byte ReadUi8()
    {
        Align();
        return _data[_bytePos++];
    }

    public ushort ReadUi16()
    {
        Align();
        var v = (ushort)(_data[_bytePos] | (_data[_bytePos + 1] << 8));
        _bytePos += 2;
        return v;
    }

    public (byte R, byte G, byte B, byte A) ReadRgba()
    {
        Align();
        var r = _data[_bytePos++];
        var g = _data[_bytePos++];
        var b = _data[_bytePos++];
        var a = _data[_bytePos++];
        return (r, g, b, a);
    }

    public (byte R, byte G, byte B, byte A) ReadRgb()
    {
        Align();
        var r = _data[_bytePos++];
        var g = _data[_bytePos++];
        var b = _data[_bytePos++];
        return (r, g, b, 255);
    }
}
