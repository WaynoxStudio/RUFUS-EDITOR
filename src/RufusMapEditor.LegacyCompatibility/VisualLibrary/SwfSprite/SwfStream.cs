using System.IO.Compression;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal readonly record struct SwfTagSlice(int Code, byte[] Data);

internal static class SwfStream
{
    public static byte[] Decompress(byte[] file)
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

    public static int SkipMovieHeader(byte[] body)
    {
        var br = new SwfBitReader(body);
        _ = br.ReadRect();
        return br.BytePosition + 4;
    }

    public static IEnumerable<SwfTagSlice> EnumerateTags(byte[] buffer, int start, int end)
    {
        var pos = start;
        var tags = 0;
        while (pos < end - 1 && tags < SwfSpriteLimits.MaxTagsPerTimeline)
        {
            tags++;
            var codeAndLen = BitConverter.ToUInt16(buffer, pos);
            pos += 2;
            var code = codeAndLen >> 6;
            var length = codeAndLen & 0x3F;
            if (length == 0x3F)
            {
                if (pos + 4 > end)
                    yield break;
                length = BitConverter.ToInt32(buffer, pos);
                pos += 4;
            }

            if (length < 0 || pos + length > end)
                yield break;

            var data = buffer.AsSpan(pos, length).ToArray();
            pos += length;
            yield return new SwfTagSlice(code, data);
            if (code == 0)
                yield break;
        }
    }
}
