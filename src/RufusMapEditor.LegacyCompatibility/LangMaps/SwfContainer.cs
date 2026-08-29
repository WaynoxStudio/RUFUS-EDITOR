using System.IO.Compression;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

internal sealed class SwfTag
{
    public required int Code { get; init; }
    public required byte[] Data { get; init; }
}

internal sealed class SwfContainer
{
    public required byte Version { get; init; }
    public required ushort FrameRateFixed { get; init; }
    public required ushort FrameCount { get; init; }
    public required byte[] FrameSizeRectBytes { get; init; }
    public required IReadOnlyList<SwfTag> Tags { get; init; }
    public required bool WasCompressed { get; init; }

    public static SwfContainer Read(byte[] fileBytes)
    {
        if (fileBytes.Length < 8)
            throw new InvalidOperationException("SWF demasiado corto.");
        if (fileBytes[1] != (byte)'W' || fileBytes[2] != (byte)'S')
            throw new InvalidOperationException("Cabecera SWF inválida.");

        var compressed = fileBytes[0] == (byte)'C';
        if (fileBytes[0] is not ((byte)'F' or (byte)'C'))
            throw new InvalidOperationException($"Firma SWF no soportada: {(char)fileBytes[0]}WS (solo FWS/CWS).");

        var version = fileBytes[3];
        var declaredLength = BitConverter.ToInt32(fileBytes, 4);
        byte[] body;
        if (compressed)
        {
            using var zs = new ZLibStream(new MemoryStream(fileBytes, 8, fileBytes.Length - 8), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            zs.CopyTo(ms);
            body = ms.ToArray();
        }
        else
        {
            body = fileBytes[8..];
        }

        if (body.Length + 8 != declaredLength)
            throw new InvalidOperationException(
                $"Longitud SWF inconsistente: cabecera={declaredLength}, real={body.Length + 8}.");

        var rectLen = MeasureRectBytes(body);
        var frameSize = body[..rectLen];
        var o = rectLen;
        var frameRate = BitConverter.ToUInt16(body, o);
        o += 2;
        var frameCount = BitConverter.ToUInt16(body, o);
        o += 2;

        var tags = new List<SwfTag>();
        while (o < body.Length)
        {
            var codeAndLen = BitConverter.ToUInt16(body, o);
            var code = codeAndLen >> 6;
            var length = codeAndLen & 0x3F;
            var hdr = 2;
            if (length == 0x3F)
            {
                length = BitConverter.ToInt32(body, o + 2);
                hdr = 6;
            }

            var data = body.AsSpan(o + hdr, length).ToArray();
            tags.Add(new SwfTag { Code = code, Data = data });
            o += hdr + length;
            if (code == 0)
                break;
        }

        return new SwfContainer
        {
            Version = version,
            FrameRateFixed = frameRate,
            FrameCount = frameCount,
            FrameSizeRectBytes = frameSize,
            Tags = tags,
            WasCompressed = compressed,
        };
    }

    public byte[] Write(bool compress)
    {
        using var bodyMs = new MemoryStream();
        bodyMs.Write(FrameSizeRectBytes);
        bodyMs.Write(BitConverter.GetBytes(FrameRateFixed));
        bodyMs.Write(BitConverter.GetBytes(FrameCount));
        foreach (var tag in Tags)
            WriteTag(bodyMs, tag.Code, tag.Data);

        var body = bodyMs.ToArray();
        var totalLen = body.Length + 8;
        byte[] payload;
        if (compress)
        {
            using var ms = new MemoryStream();
            using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                zs.Write(body);
            payload = ms.ToArray();
        }
        else
        {
            payload = body;
        }

        var file = new byte[8 + payload.Length];
        file[0] = (byte)(compress ? 'C' : 'F');
        file[1] = (byte)'W';
        file[2] = (byte)'S';
        file[3] = Version;
        BitConverter.TryWriteBytes(file.AsSpan(4, 4), totalLen);
        payload.CopyTo(file.AsSpan(8));
        return file;
    }

    public SwfContainer WithReplacedTag(int tagIndex, byte[] newData)
    {
        var tags = Tags.ToList();
        tags[tagIndex] = new SwfTag { Code = tags[tagIndex].Code, Data = newData };
        return new SwfContainer
        {
            Version = Version,
            FrameRateFixed = FrameRateFixed,
            FrameCount = FrameCount,
            FrameSizeRectBytes = FrameSizeRectBytes,
            Tags = tags,
            WasCompressed = WasCompressed,
        };
    }

    private static void WriteTag(Stream stream, int code, byte[] data)
    {
        if (data.Length < 0x3F)
        {
            stream.Write(BitConverter.GetBytes((ushort)((code << 6) | data.Length)));
        }
        else
        {
            stream.Write(BitConverter.GetBytes((ushort)((code << 6) | 0x3F)));
            stream.Write(BitConverter.GetBytes(data.Length));
        }

        stream.Write(data);
    }

    private static int MeasureRectBytes(ReadOnlySpan<byte> body)
    {
        var nBits = body[0] >> 3;
        return (5 + nBits * 4 + 7) / 8;
    }
}
