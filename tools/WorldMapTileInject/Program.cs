using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: WorldMapTileInject <isla.png> <x> <y> [0.swf] [0111.swf]");
    return 1;
}

var pngPath = args[0];
var worldX = int.Parse(args[1]);
var worldY = int.Parse(args[2]);
var exportName = $"{worldX}_{worldY}";
var targets = args.Length > 3
    ? args.Skip(3).ToArray()
    : new[]
    {
        @"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient\clips\maps\0.swf",
        @"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient\clips\maps\0111.swf",
    };

using var src = new Bitmap(pngPath);
if (src.Width != 600 || src.Height != 345)
{
    Console.Error.WriteLine($"Expected 600x345, got {src.Width}x{src.Height}");
    return 2;
}

using var tile = PrepareTileWithTransparentOcean(src);
var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
var previewDir = Path.Combine(Path.GetTempPath(), "worldmap_inject");
Directory.CreateDirectory(previewDir);
var previewPath = Path.Combine(previewDir, $"{exportName}_{stamp}.png");
tile.Save(previewPath, ImageFormat.Png);
Console.WriteLine($"Preview: {previewPath}");

foreach (var swfPath in targets)
{
    if (!File.Exists(swfPath))
    {
        Console.WriteLine($"SKIP missing: {swfPath}");
        continue;
    }

    var backup = swfPath + $".bak_{stamp}";
    File.Copy(swfPath, backup, overwrite: false);
    Console.WriteLine($"Backup: {backup}");

    var swf = SwfFile.Read(File.ReadAllBytes(swfPath));
    if (swf.HasExport(exportName))
    {
        Console.Error.WriteLine($"Export '{exportName}' already exists in {swfPath}");
        return 3;
    }

    var nextId = swf.MaxCharacterId() + 1;
    // Match existing cadence: sprite, bitmap=sprite+1, shape=sprite+2 when possible.
    // Allocate three fresh IDs in order bitmap, shape, sprite for simpler wiring.
    var bitmapId = (ushort)nextId;
    var shapeId = (ushort)(nextId + 1);
    var spriteId = (ushort)(nextId + 2);

    var bitmapTag = BuildDefineBitsLossless2(bitmapId, tile);
    var shapeTag = BuildBitmapShape(shapeId, bitmapId, template: swf.FirstDefineShape2Body());
    var spriteTag = BuildSprite(spriteId, shapeId, template: swf.FirstDefineSpriteBody());
    var exportTag = BuildExport(spriteId, exportName);

    swf.InsertBeforeEnd(bitmapTag, shapeTag, spriteTag, exportTag);
    File.WriteAllBytes(swfPath, swf.Write(compress: true));
    Console.WriteLine($"OK {Path.GetFileName(swfPath)} → export '{exportName}' ids bmp={bitmapId} shape={shapeId} sprite={spriteId}");
}

Console.WriteLine("Done. Restart client / reload worldmap to see it. Move later = re-export under new X_Y.");
return 0;

static Bitmap PrepareTileWithTransparentOcean(Bitmap src)
{
    var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
        g.DrawImage(src, 0, 0, src.Width, src.Height);

    var water = src.GetPixel(0, 0);
    var tolerance = 28;
    var w = bmp.Width;
    var h = bmp.Height;
    var mark = new bool[w * h];
    var q = new Queue<Point>();

    void TryEnqueue(int x, int y)
    {
        if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
        var i = y * w + x;
        if (mark[i]) return;
        var c = bmp.GetPixel(x, y);
        if (!IsOcean(c, water, tolerance)) return;
        mark[i] = true;
        q.Enqueue(new Point(x, y));
    }

    TryEnqueue(0, 0);
    TryEnqueue(w - 1, 0);
    TryEnqueue(0, h - 1);
    TryEnqueue(w - 1, h - 1);

    while (q.Count > 0)
    {
        var p = q.Dequeue();
        TryEnqueue(p.X + 1, p.Y);
        TryEnqueue(p.X - 1, p.Y);
        TryEnqueue(p.X, p.Y + 1);
        TryEnqueue(p.X, p.Y - 1);
    }

    // Also clear wave dashes (near-white) that sit on cleared ocean neighborhood.
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var i = y * w + x;
        if (mark[i]) continue;
        var c = bmp.GetPixel(x, y);
        if (!IsWaveDash(c)) continue;
        if (HasMarkedNeighbor(mark, w, h, x, y))
            mark[i] = true;
    }

    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        if (!mark[y * w + x]) continue;
        var c = bmp.GetPixel(x, y);
        bmp.SetPixel(x, y, Color.FromArgb(0, c.R, c.G, c.B));
    }

    return bmp;
}

static bool HasMarkedNeighbor(bool[] mark, int w, int h, int x, int y)
{
    for (var dy = -2; dy <= 2; dy++)
    for (var dx = -2; dx <= 2; dx++)
    {
        var nx = x + dx;
        var ny = y + dy;
        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
        if (mark[ny * w + nx]) return true;
    }

    return false;
}

static bool IsOcean(Color c, Color water, int tolerance)
{
    var dr = Math.Abs(c.R - water.R);
    var dg = Math.Abs(c.G - water.G);
    var db = Math.Abs(c.B - water.B);
    if (dr <= tolerance && dg <= tolerance && db <= tolerance) return true;
    // Soft foam / antialias near water hue
    return dr + dg + db <= tolerance * 2 && c.B >= c.R - 5 && c.G >= c.R - 5;
}

static bool IsWaveDash(Color c) =>
    c.R >= 200 && c.G >= 200 && c.B >= 200 && Math.Abs(c.R - c.G) < 25 && Math.Abs(c.G - c.B) < 25;

static SwfTag BuildDefineBitsLossless2(ushort id, Bitmap bmp)
{
    var w = bmp.Width;
    var h = bmp.Height;
    var argb = new byte[w * h * 4];
    var rect = new Rectangle(0, 0, w, h);
    var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try
    {
        var stride = bd.Stride;
        var row = new byte[Math.Abs(stride)];
        for (var y = 0; y < h; y++)
        {
            Marshal.Copy(IntPtr.Add(bd.Scan0, y * stride), row, 0, row.Length);
            for (var x = 0; x < w; x++)
            {
                var bi = x * 4;
                var oi = (y * w + x) * 4;
                var b = row[bi];
                var g = row[bi + 1];
                var r = row[bi + 2];
                var a = row[bi + 3];
                argb[oi] = a;
                argb[oi + 1] = r;
                argb[oi + 2] = g;
                argb[oi + 3] = b;
            }
        }
    }
    finally
    {
        bmp.UnlockBits(bd);
    }

    var zlib = ZlibCompress(argb);
    using var ms = new MemoryStream();
    ms.Write(BitConverter.GetBytes(id));
    ms.WriteByte(5); // 32-bit ARGB
    ms.Write(BitConverter.GetBytes((ushort)w));
    ms.Write(BitConverter.GetBytes((ushort)h));
    ms.Write(zlib);
    return new SwfTag(36, ms.ToArray());
}

static SwfTag BuildBitmapShape(ushort shapeId, ushort bitmapId, byte[] template)
{
    // template includes character id at [0..1] and bitmap id at offset 18 (from prior audit).
    var data = (byte[])template.Clone();
    BitConverter.TryWriteBytes(data.AsSpan(0, 2), shapeId);
    const int bitmapIdOffset = 18;
    if (data.Length < bitmapIdOffset + 2)
        throw new InvalidOperationException("Shape template too short.");
    BitConverter.TryWriteBytes(data.AsSpan(bitmapIdOffset, 2), bitmapId);
    return new SwfTag(22, data); // DefineShape2
}

static SwfTag BuildSprite(ushort spriteId, ushort shapeId, byte[] template)
{
    var data = (byte[])template.Clone();
    BitConverter.TryWriteBytes(data.AsSpan(0, 2), spriteId);
    // PlaceObject2 character id at bytes 9..10 in audited template.
    const int charIdOffset = 9;
    if (data.Length < charIdOffset + 2)
        throw new InvalidOperationException("Sprite template too short.");
    BitConverter.TryWriteBytes(data.AsSpan(charIdOffset, 2), shapeId);
    return new SwfTag(39, data); // DefineSprite
}

static SwfTag BuildExport(ushort characterId, string name)
{
    using var ms = new MemoryStream();
    ms.Write(BitConverter.GetBytes((ushort)1)); // count
    ms.Write(BitConverter.GetBytes(characterId));
    var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
    ms.Write(nameBytes);
    ms.WriteByte(0);
    return new SwfTag(56, ms.ToArray()); // ExportAssets
}

static byte[] ZlibCompress(byte[] raw)
{
    using var ms = new MemoryStream();
    using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        zs.Write(raw);
    return ms.ToArray();
}

internal readonly record struct SwfTag(int Code, byte[] Data);

internal sealed class SwfFile
{
    public required byte Version { get; init; }
    public required ushort FrameRateFixed { get; init; }
    public required ushort FrameCount { get; init; }
    public required byte[] FrameSizeRectBytes { get; init; }
    public required List<SwfTag> Tags { get; init; }

    public static SwfFile Read(byte[] fileBytes)
    {
        if (fileBytes.Length < 8 || fileBytes[1] != (byte)'W' || fileBytes[2] != (byte)'S')
            throw new InvalidOperationException("Invalid SWF.");
        var compressed = fileBytes[0] == (byte)'C';
        var version = fileBytes[3];
        byte[] body;
        if (compressed)
        {
            using var zs = new ZLibStream(new MemoryStream(fileBytes, 8, fileBytes.Length - 8), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            zs.CopyTo(ms);
            body = ms.ToArray();
        }
        else body = fileBytes[8..];

        var rectLen = MeasureRectBytes(body);
        var o = rectLen;
        var frameRate = BitConverter.ToUInt16(body, o); o += 2;
        var frameCount = BitConverter.ToUInt16(body, o); o += 2;
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
            tags.Add(new SwfTag(code, data));
            o += hdr + length;
            if (code == 0) break;
        }

        return new SwfFile
        {
            Version = version,
            FrameRateFixed = frameRate,
            FrameCount = frameCount,
            FrameSizeRectBytes = body[..rectLen],
            Tags = tags,
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
        else payload = body;

        var file = new byte[8 + payload.Length];
        file[0] = (byte)(compress ? 'C' : 'F');
        file[1] = (byte)'W';
        file[2] = (byte)'S';
        file[3] = Version;
        BitConverter.TryWriteBytes(file.AsSpan(4, 4), totalLen);
        payload.CopyTo(file.AsSpan(8));
        return file;
    }

    public bool HasExport(string name)
    {
        foreach (var tag in Tags)
        {
            if (tag.Code != 56 || tag.Data.Length < 2) continue;
            var count = BitConverter.ToUInt16(tag.Data, 0);
            var p = 2;
            for (var i = 0; i < count; i++)
            {
                if (p + 2 >= tag.Data.Length) break;
                p += 2;
                var end = p;
                while (end < tag.Data.Length && tag.Data[end] != 0) end++;
                var n = System.Text.Encoding.ASCII.GetString(tag.Data, p, end - p);
                if (string.Equals(n, name, StringComparison.Ordinal)) return true;
                p = end + 1;
            }
        }

        return false;
    }

    public int MaxCharacterId()
    {
        var max = 0;
        foreach (var tag in Tags)
        {
            if (tag.Data.Length < 2) continue;
            if (tag.Code is 21 or 22 or 32 or 35 or 36 or 39 or 83)
            {
                var id = BitConverter.ToUInt16(tag.Data, 0);
                if (id > max) max = id;
            }
        }

        return max;
    }

    public byte[] FirstDefineShape2Body() =>
        Tags.First(t => t.Code == 22 && t.Data.Length >= 20).Data;

    public byte[] FirstDefineSpriteBody() =>
        Tags.First(t => t.Code == 39 && t.Data.Length >= 12).Data;

    public void InsertBeforeEnd(params SwfTag[] newTags)
    {
        // Character tags must precede the root ShowFrame + End.
        var showIndex = Tags.FindLastIndex(t => t.Code == 1);
        if (showIndex < 0)
            showIndex = Tags.FindLastIndex(t => t.Code == 0);
        if (showIndex < 0)
            showIndex = Tags.Count;
        Tags.InsertRange(showIndex, newTags);
    }

    private static void WriteTag(Stream stream, int code, byte[] data)
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

    private static int MeasureRectBytes(ReadOnlySpan<byte> body)
    {
        var nBits = body[0] >> 3;
        return (5 + nBits * 4 + 7) / 8;
    }
}
