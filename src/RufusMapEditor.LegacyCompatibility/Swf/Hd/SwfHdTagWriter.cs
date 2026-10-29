namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

internal static class SwfHdTagWriter
{
    public static void WriteTag(Stream stream, int code, byte[] data)
    {
        if (data.Length < 0x3F)
            stream.Write(BitConverter.GetBytes((ushort)((code << 6) | data.Length)));
        else
        {
            stream.Write(BitConverter.GetBytes((ushort)((code << 6) | 0x3F)));
            stream.Write(BitConverter.GetBytes(data.Length));
        }

        stream.Write(data);
    }

    public static byte[] Tag(int code, byte[] data)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, code, data);
        return ms.ToArray();
    }
}
