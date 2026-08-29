using System.IO.Compression;
using System.Text;

static byte[] DecompressBody(byte[] file)
{
    if (file.Length < 8 || file[1] != (byte)'W' || file[2] != (byte)'S')
        throw new InvalidOperationException("Invalid SWF");
    if (file[0] == (byte)'F') return file[8..];
    using var zs = new ZLibStream(new MemoryStream(file, 8, file.Length - 8), CompressionMode.Decompress);
    using var ms = new MemoryStream();
    zs.CopyTo(ms);
    return ms.ToArray();
}

static (int FrameCount, List<(int Code, int Len, string Detail)> Tags) AuditSwf(byte[] file)
{
    var body = DecompressBody(file);
    var tags = new List<(int Code, int Len, string Detail)>();
    var pos = 0;
    // skip rect (variable) — read first byte for nbits
    var first = body[0];
    var nbits = (first >> 3) & 0x1F;
    var rectBits = 5 + nbits * 4;
    pos = (rectBits + 7) / 8;
    var frameCount = pos + 4 <= body.Length ? BitConverter.ToUInt16(body, pos + 2) : (ushort)0;
    pos += 4;

    while (pos < body.Length - 1)
    {
        var codeAndLen = BitConverter.ToUInt16(body, pos);
        pos += 2;
        var code = codeAndLen >> 6;
        var length = codeAndLen & 0x3F;
        if (length == 0x3F)
        {
            length = BitConverter.ToInt32(body, pos);
            pos += 4;
        }
        if (length < 0 || pos + length > body.Length) break;
        var data = body.AsSpan(pos, length);
        var detail = DescribeTag(code, data);
        tags.Add((code, length, detail));
        pos += length;
        if (code == 0) break;
    }

    return (frameCount, tags);
}

static string DescribeTag(int code, ReadOnlySpan<byte> data)
{
    return code switch
    {
        39 => DescribeDefineSprite(data),
        26 or 70 => DescribePlaceObject(code, data),
        32 or 22 or 83 => DescribeDefineShape(code, data),
        33 => DescribeDefineShape(code, data),
        34 => $"DefineMorphShape charId={BitConverter.ToUInt16(data)}",
        36 => data.Length >= 7 ? $"DefineBitsLossless2 {BitConverter.ToUInt16(data.ToArray(), 3)}x{BitConverter.ToUInt16(data.ToArray(), 5)}" : "DefineBitsLossless2",
        21 or 35 => $"DefineBitsJPEG{(code == 35 ? "3" : "2")} charId={BitConverter.ToUInt16(data.ToArray(), 0)}",
        1 => "ShowFrame",
        76 => "SymbolClass",
        82 => "DoABC",
        12 => "DoAction",
        2 => "DefineShape",
        _ => TagName(code),
    };
}

static string TagName(int code) => code switch
{
    0 => "End",
    1 => "ShowFrame",
    2 => "DefineShape",
    9 => "SetBackgroundColor",
    10 => "DefineFont",
    11 => "DefineText",
    20 => "DefineBitsLossless",
    21 => "DefineBitsJPEG2",
    22 => "DefineShape2",
    26 => "PlaceObject2",
    28 => "RemoveObject",
    32 => "DefineShape3",
    33 => "DefineShape4",
    34 => "DefineMorphShape",
    35 => "DefineBitsJPEG3",
    36 => "DefineBitsLossless2",
    37 => "DefineText2",
    39 => "DefineSprite",
    40 => "NameCharacter",
    46 => "DefineMorphShape2",
    69 => "FileAttributes",
    70 => "PlaceObject3",
    71 => "RemoveObject2",
    76 => "SymbolClass",
    82 => "DoABC",
    83 => "DefineShape4",
    _ => $"Tag{code}",
};

static string DescribeDefineShape(int code, ReadOnlySpan<byte> data)
{
    if (data.Length < 2) return TagName(code);
    var charId = BitConverter.ToUInt16(data.ToArray(), 0);
    return $"{TagName(code)} charId={charId} len={data.Length}";
}

static string DescribeDefineSprite(ReadOnlySpan<byte> data)
{
    if (data.Length < 4) return "DefineSprite";
    var arr = data.ToArray();
    var charId = BitConverter.ToUInt16(arr, 0);
    var frameCount = BitConverter.ToUInt16(arr, 2);
    return $"DefineSprite charId={charId} frames={frameCount} payload={data.Length}b";
}

static string DescribePlaceObject(int code, ReadOnlySpan<byte> data)
{
    if (data.Length == 0) return TagName(code);
    var flags = data[0];
    var hasChar = (flags & 0x02) != 0;
    var hasMatrix = (flags & 0x08) != 0;
    var hasCxform = (flags & 0x10) != 0;
    var hasRatio = (flags & 0x04) != 0;
    var hasName = (flags & 0x20) != 0;
    var hasClip = (flags & 0x40) != 0;
    var hasClipDepth = code == 70 && data.Length > 1 && (data[1] & 0x80) != 0;
    var parts = new List<string> { TagName(code) };
    if (hasChar) parts.Add("char");
    if (hasMatrix) parts.Add("matrix");
    if (hasCxform) parts.Add("cxform");
    if (hasRatio) parts.Add("ratio");
    if (hasName) parts.Add("name");
    if (hasClip || hasClipDepth) parts.Add("clip");
    return string.Join("+", parts);
}

static void PrintAudit(string label, string path)
{
    Console.WriteLine($"\n========== {label} ==========");
    Console.WriteLine(path);
    if (!File.Exists(path)) { Console.WriteLine("MISSING"); return; }
    var file = File.ReadAllBytes(path);
    Console.WriteLine($"File: {file.Length} bytes, header: {(char)file[0]}{(char)file[1]}{(char)file[2]} v{file[3]}");
    var (frames, tags) = AuditSwf(file);
    Console.WriteLine($"Header frameCount: {frames}");
    var grouped = tags.GroupBy(t => t.Code).OrderBy(g => g.Key)
        .Select(g => $"{TagName(g.Key)}({g.Key}) x{g.Count()}");
    Console.WriteLine("Tag summary: " + string.Join(", ", grouped));

    foreach (var t in tags.Where(t => t.Code is 39 or 1 or 76 or 82 or 12))
        Console.WriteLine($"  [{TagName(t.Code)}] {t.Detail} len={t.Len}");

    var shapes = tags.Where(t => t.Code is 2 or 22 or 32 or 33 or 83).ToList();
    Console.WriteLine($"DefineShape* total: {shapes.Count} (codes: {string.Join(",", shapes.Select(s => s.Code).Distinct())})");

    var places = tags.Count(t => t.Code is 26 or 70);
    var showFrames = tags.Count(t => t.Code == 1);
    var sprites = tags.Where(t => t.Code == 39).ToList();
    Console.WriteLine($"PlaceObject*: {places}, ShowFrame: {showFrames}, DefineSprite: {sprites.Count}");
    if (sprites.Count > 0)
        Console.WriteLine("  Sprites: " + string.Join("; ", sprites.Select(s => s.Detail)));

    // Frame 0 composition: tags until first ShowFrame after first PlaceObject block
    var firstShow = tags.FindIndex(t => t.Code == 1);
    if (firstShow >= 0)
    {
        var frame0 = tags.Take(firstShow + 1).Where(t => t.Code is 26 or 70 or 39 or 32 or 22 or 33).ToList();
        Console.WriteLine($"Until 1st ShowFrame: {string.Join(" | ", frame0.Select(f => f.Detail))}");
    }
}

var clips = args.Length > 0 ? args[0]
    : @"C:\Users\rubez\Desktop\RUFUS RETRO\resources\app\retroclient\clips";
var ids = new[] { 10, 20, 30, 40, 71, 120, 1245, 9073 };

Console.WriteLine("SWF TAG AUDIT — ADMIN.UI.4B.2A.3D");
foreach (var id in ids)
{
    PrintAudit($"SPRITE {id}", Path.Combine(clips, "sprites", $"{id}.swf"));
    PrintAudit($"ARTWORK {id}", Path.Combine(clips, "artworks", "big", $"{id}.swf"));
}

// Rasterizer behavior probe
Console.WriteLine("\n========== RASTERIZER PROBE (artworks/big) ==========");
foreach (var id in ids)
{
    var artPath = Path.Combine(clips, "artworks", "big", $"{id}.swf");
    if (!File.Exists(artPath)) continue;
    try
    {
        var png = RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfArtworkRasterizer.RasterizeToPng(File.ReadAllBytes(artPath), 96);
        Console.WriteLine($"GFX {id} artwork raster OK -> {png.Length} bytes PNG");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"GFX {id} artwork raster FAIL -> {ex.Message}");
    }
}

Console.WriteLine("\n========== RASTERIZER PROBE (sprites) ==========");
foreach (var id in ids)
{
    var sprPath = Path.Combine(clips, "sprites", $"{id}.swf");
    if (!File.Exists(sprPath)) continue;
    try
    {
        var png = RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfArtworkRasterizer.RasterizeToPng(File.ReadAllBytes(sprPath), 96);
        Console.WriteLine($"GFX {id} sprite raster OK -> {png.Length} bytes PNG");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"GFX {id} sprite raster FAIL -> {ex.Message}");
    }
}

// sprites.xml name count
var spritesXml = Path.Combine(clips, "sprites", "sprites.xml");
if (File.Exists(spritesXml))
{
    var parsed = RufusMapEditor.LegacyCompatibility.VisualLibrary.SpritesXmlParser.ParseFile(spritesXml);
    Console.WriteLine($"\n========== sprites.xml ==========");
    Console.WriteLine($"Parsed names: {parsed.Names.Count}, warnings: {parsed.Warnings.Count}");
    foreach (var id in ids)
        Console.WriteLine($"  {id} -> {(parsed.Names.TryGetValue(id, out var n) ? n : "(missing)")}");
}
