using System.Drawing;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Swf.Hd;

public static class SwfHdAnimationBuilder
{
    /// <summary>
    /// Builds a sprite whose frames share the same logical bounds/anchor.
    /// <paramref name="boomerang"/> expands 1..n..2 (e.g. 6 images → 1-2-3-4-5-6-5-4-3-2).
    /// </summary>
    public static byte[] BuildBoomerangSprite(
        IReadOnlyList<Bitmap> frames,
        GfxHdLayout layout,
        ushort firstBitmapId,
        ushort firstShapeId,
        ushort spriteId,
        string exportName,
        bool boomerang = true)
    {
        if (frames is null || frames.Count == 0)
            throw new ArgumentException("Hace falta al menos un frame HD.", nameof(frames));

        foreach (var frame in frames)
        {
            if (frame.Width != layout.SourcePixelWidth || frame.Height != layout.SourcePixelHeight)
            {
                throw new InvalidOperationException(
                    $"Todos los frames deben medir {layout.SourcePixelWidth}×{layout.SourcePixelHeight} (fuente HD). " +
                    $"Encontrado {frame.Width}×{frame.Height}.");
            }
        }

        using var ms = new MemoryStream();
        var shapeIds = new ushort[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var bitmapId = (ushort)(firstBitmapId + i);
            var shapeId = (ushort)(firstShapeId + i);
            shapeIds[i] = shapeId;
            var bmpTag = SwfHdBitmapEncoder.EncodeDefineBitsLossless2(bitmapId, frames[i]);
            var shapeTag = SwfHdShapeEncoder.EncodeBitmapFillShape(shapeId, bitmapId, layout);
            ms.Write(bmpTag);
            ms.Write(shapeTag);
        }

        var sequence = BuildSequence(frames.Count, boomerang);
        using var spriteBody = new MemoryStream();
        spriteBody.Write(BitConverter.GetBytes(spriteId));
        spriteBody.Write(BitConverter.GetBytes((ushort)sequence.Count));
        for (var i = 0; i < sequence.Count; i++)
        {
            var flags = (byte)(i == 0 ? 0x06 : 0x07); // first: has char+matrix; later: move+char+matrix
            var place = EncodePlace(flags, depth: 1, shapeIds[sequence[i]]);
            spriteBody.Write(place);
            SwfHdTagWriter.WriteTag(spriteBody, 1, Array.Empty<byte>());
        }

        SwfHdTagWriter.WriteTag(spriteBody, 0, Array.Empty<byte>());
        ms.Write(SwfHdTagWriter.Tag(39, spriteBody.ToArray()));
        ms.Write(SwfHdSymbolBuilder.EncodeExport(spriteId, exportName));
        return ms.ToArray();
    }

    public static IReadOnlyList<int> BuildSequence(int frameCount, bool boomerang)
    {
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        var list = new List<int>(frameCount * 2);
        for (var i = 0; i < frameCount; i++)
            list.Add(i);
        if (boomerang && frameCount > 1)
        {
            for (var i = frameCount - 2; i >= 1; i--)
                list.Add(i);
        }

        return list;
    }

    private static byte[] EncodePlace(byte flags, int depth, ushort characterId)
    {
        var bw = new SwfBitWriter();
        bw.WriteUi8(flags);
        bw.WriteUi16(depth);
        bw.WriteUi16(characterId);
        bw.WriteIdentityTranslateMatrix();
        return SwfHdTagWriter.Tag(26, bw.ToArray());
    }
}
