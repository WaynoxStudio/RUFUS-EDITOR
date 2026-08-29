namespace RufusMapEditor.LegacyCompatibility.Gfx;

/// <summary>
/// Reads native pixel dimensions from image headers without full decode (PNG IHDR).
/// </summary>
public static class GfxImageDimensions
{
    public static (int Width, int Height)? TryRead(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".png" => ReadPng(filePath),
                ".jpg" or ".jpeg" => ReadJpeg(filePath),
                ".bmp" => ReadBmp(filePath),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static (int Width, int Height)? ReadPng(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var fs = File.OpenRead(path);
        if (fs.Read(header) < 24)
            return null;
        if (header[0] != 0x89 || header[1] != (byte)'P')
            return null;
        var w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return w > 0 && h > 0 ? (w, h) : null;
    }

    private static (int Width, int Height)? ReadJpeg(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.ReadByte() != 0xFF || fs.ReadByte() != 0xD8)
            return null;
        while (fs.Position < fs.Length)
        {
            if (fs.ReadByte() != 0xFF)
                continue;
            var marker = fs.ReadByte();
            if (marker is 0xC0 or 0xC2)
            {
                fs.Seek(3, SeekOrigin.Current);
                var h = (fs.ReadByte() << 8) | fs.ReadByte();
                var w = (fs.ReadByte() << 8) | fs.ReadByte();
                return w > 0 && h > 0 ? (w, h) : null;
            }

            var len = (fs.ReadByte() << 8) | fs.ReadByte();
            if (len < 2)
                return null;
            fs.Seek(len - 2, SeekOrigin.Current);
        }

        return null;
    }

    private static (int Width, int Height)? ReadBmp(string path)
    {
        Span<byte> header = stackalloc byte[26];
        using var fs = File.OpenRead(path);
        if (fs.Read(header) < 26)
            return null;
        var w = header[18] | (header[19] << 8) | (header[20] << 16) | (header[21] << 24);
        var h = header[22] | (header[23] << 8) | (header[24] << 16) | (header[25] << 24);
        h = Math.Abs(h);
        return w > 0 && h > 0 ? (w, h) : null;
    }
}
