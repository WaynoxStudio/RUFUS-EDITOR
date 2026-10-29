using System.Drawing;
using System.Text;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

public sealed class SwfHdSymbolTags
{
    public required byte[] BitmapTag { get; init; }
    public required byte[] ShapeTag { get; init; }
    public required byte[] SpriteTag { get; init; }
    public required byte[] ExportTag { get; init; }
    public required int BitmapWidth { get; init; }
    public required int BitmapHeight { get; init; }
    public required GfxHdLayout Layout { get; init; }

    public byte[] Concat()
    {
        using var ms = new MemoryStream();
        ms.Write(BitmapTag);
        ms.Write(ShapeTag);
        ms.Write(SpriteTag);
        ms.Write(ExportTag);
        return ms.ToArray();
    }
}

public static class SwfHdSymbolBuilder
{
    public static SwfHdSymbolTags BuildStatic(
        Bitmap source,
        GfxHdLayout layout,
        ushort bitmapId,
        ushort shapeId,
        ushort spriteId,
        string exportName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(layout);
        SwfHdBitmapEncoder.EnsureMatchesLayout(source, layout);
        var bmpTag = SwfHdBitmapEncoder.EncodeDefineBitsLossless2(bitmapId, source);
        var shapeTag = SwfHdShapeEncoder.EncodeBitmapFillShape(shapeId, bitmapId, layout);
        var spriteTag = EncodeSprite(spriteId, shapeId);
        var exportTag = EncodeExport(spriteId, exportName);
        return new SwfHdSymbolTags
        {
            BitmapTag = bmpTag,
            ShapeTag = shapeTag,
            SpriteTag = spriteTag,
            ExportTag = exportTag,
            BitmapWidth = source.Width,
            BitmapHeight = source.Height,
            Layout = layout,
        };
    }

    public static byte[] EncodeSprite(ushort spriteId, ushort characterId, int frameCount = 1)
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(spriteId));
        ms.Write(BitConverter.GetBytes((ushort)Math.Max(1, frameCount)));
        var place = EncodePlaceObject2(characterId, depth: 1);
        ms.Write(place);
        SwfHdTagWriter.WriteTag(ms, 1, Array.Empty<byte>());
        SwfHdTagWriter.WriteTag(ms, 0, Array.Empty<byte>());
        return SwfHdTagWriter.Tag(39, ms.ToArray());
    }

    public static byte[] EncodePlaceObject2(ushort characterId, int depth)
    {
        var bw = new SwfBitWriter();
        bw.WriteUi8(0x06);
        bw.WriteUi16(depth);
        bw.WriteUi16(characterId);
        bw.WriteIdentityTranslateMatrix();
        return SwfHdTagWriter.Tag(26, bw.ToArray());
    }

    public static byte[] EncodeExport(ushort characterId, string name)
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(BitConverter.GetBytes(characterId));
        ms.Write(Encoding.ASCII.GetBytes(name));
        ms.WriteByte(0);
        return SwfHdTagWriter.Tag(56, ms.ToArray());
    }

    public static byte[] WrapMovie(IReadOnlyList<byte> tags, int frameWidthTwips, int frameHeightTwips)
    {
        var header = new SwfBitWriter();
        header.WriteRect(0, Math.Max(1, frameWidthTwips), 0, Math.Max(1, frameHeightTwips));
        using var body = new MemoryStream();
        body.Write(header.ToArray());
        body.Write(BitConverter.GetBytes((ushort)(12 << 8)));
        body.Write(BitConverter.GetBytes((ushort)1));
        body.Write(tags as byte[] ?? tags.ToArray());
        SwfHdTagWriter.WriteTag(body, 1, Array.Empty<byte>());
        SwfHdTagWriter.WriteTag(body, 0, Array.Empty<byte>());
        var bodyBytes = body.ToArray();
        using var compressed = new MemoryStream();
        using (var zs = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            zs.Write(bodyBytes);
        var payload = compressed.ToArray();
        var file = new byte[8 + payload.Length];
        file[0] = (byte)'C';
        file[1] = (byte)'W';
        file[2] = (byte)'S';
        file[3] = 8;
        BitConverter.TryWriteBytes(file.AsSpan(4, 4), bodyBytes.Length + 8);
        payload.CopyTo(file, 8);
        return file;
    }
}
