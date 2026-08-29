namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal sealed class SwfSpriteDefinition
{
    public required int CharacterId { get; init; }
    public required int FrameCount { get; init; }
    public required byte[] TagBuffer { get; init; }
    public required int TagStart { get; init; }
    public required int TagEnd { get; init; }
    public int PayloadBytes => TagEnd - TagStart;
}

internal sealed class SwfMovie
{
    public required byte[] Body { get; init; }
    public int RootTimelineStart { get; init; }
    public int RootTimelineEnd { get; init; }
    public int FrameCount { get; init; } = 1;
    public Dictionary<int, SwfShapeDefinition> Shapes { get; } = new();
    public Dictionary<int, SwfBitmapDefinition> Bitmaps { get; } = new();
    public Dictionary<int, SwfSpriteDefinition> Sprites { get; } = new();
    public Dictionary<string, int> ExportedNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, string> ExportNamesById { get; } = new();
}

internal static class SwfMovieParser
{
    public static SwfMovie Parse(byte[] swfBytes)
    {
        var body = SwfStream.Decompress(swfBytes);
        var tagStart = SwfStream.SkipMovieHeader(body);
        var movie = new SwfMovie
        {
            Body = body,
            RootTimelineStart = tagStart,
            RootTimelineEnd = body.Length,
            FrameCount = 1,
        };

        foreach (var tag in SwfStream.EnumerateTags(body, tagStart, body.Length))
        {
            if (movie.Shapes.Count + movie.Sprites.Count + movie.Bitmaps.Count >= SwfSpriteLimits.MaxSymbols)
                break;
            IngestTag(movie, tag);
        }

        return movie;
    }

    private static void IngestTag(SwfMovie movie, SwfTagSlice tag)
    {
        switch (tag.Code)
        {
            case 2 or 22 or 32 or 33 or 83:
                try
                {
                    var shape = SwfShapeParser.Parse(tag.Code, tag.Data);
                    movie.Shapes[shape.CharacterId] = shape;
                }
                catch
                {
                    // skip malformed shape
                }
                break;
            case 20 or 21 or 35 or 36:
                try
                {
                    var bmp = SwfBitmapParser.Parse(tag.Code, tag.Data);
                    if (bmp is not null)
                        movie.Bitmaps[bmp.CharacterId] = bmp;
                }
                catch
                {
                    // skip
                }
                break;
            case 39:
                if (tag.Data.Length < 4) break;
                var id = BitConverter.ToUInt16(tag.Data, 0);
                var frames = BitConverter.ToUInt16(tag.Data, 2);
                if (frames <= 0 || frames > SwfSpriteLimits.MaxFramesPerSprite) break;
                movie.Sprites[id] = new SwfSpriteDefinition
                {
                    CharacterId = id,
                    FrameCount = frames,
                    TagBuffer = tag.Data,
                    TagStart = 4,
                    TagEnd = tag.Data.Length,
                };
                break;
            case 56:
                ParseExportAssets(movie, tag.Data);
                break;
            case 76:
                ParseSymbolClass(movie, tag.Data);
                break;
        }
    }

    private static void ParseExportAssets(SwfMovie movie, byte[] data)
    {
        if (data.Length < 2) return;
        var count = BitConverter.ToUInt16(data, 0);
        var pos = 2;
        for (var i = 0; i < count && pos + 3 <= data.Length; i++)
        {
            var charId = BitConverter.ToUInt16(data, pos);
            pos += 2;
            var start = pos;
            while (pos < data.Length && data[pos] != 0) pos++;
            if (pos >= data.Length) break;
            var name = System.Text.Encoding.UTF8.GetString(data, start, pos - start);
            pos++;
            if (string.IsNullOrWhiteSpace(name)) continue;
            movie.ExportedNames[name] = charId;
            movie.ExportNamesById[charId] = name;
        }
    }

    private static void ParseSymbolClass(SwfMovie movie, byte[] data)
    {
        if (data.Length < 2) return;
        var count = BitConverter.ToUInt16(data, 0);
        var pos = 2;
        for (var i = 0; i < count && pos + 3 <= data.Length; i++)
        {
            var charId = BitConverter.ToUInt16(data, pos);
            pos += 2;
            var start = pos;
            while (pos < data.Length && data[pos] != 0) pos++;
            if (pos >= data.Length) break;
            var name = System.Text.Encoding.UTF8.GetString(data, start, pos - start);
            pos++;
            if (string.IsNullOrWhiteSpace(name)) continue;
            movie.ExportedNames.TryAdd(name, charId);
            movie.ExportNamesById.TryAdd(charId, name);
        }
    }
}
