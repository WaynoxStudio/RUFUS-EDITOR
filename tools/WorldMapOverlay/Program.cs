using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

// Overlay isla.png on top of sprite 166 (export 0_-4), same pattern as
// sprite 151 placing shape 153 + overlay shape 155.
// Does NOT remove export 2_-57.

var pngPath = args.ElementAtOrDefault(0) ?? @"C:\Users\rubez\Desktop\isla.png";
var targets = args.Length > 1
    ? args.Skip(1).ToArray()
    : new[]
    {
        @"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient\clips\maps\0.swf",
        @"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient\clips\maps\0111.swf",
    };

const ushort SpriteId = 166;
const ushort BaseShapeId = 168;
const ushort BaseBitmapId = 167;
const string ExportName = "0_-4";

using var src = new Bitmap(pngPath);
if (src.Width != 600 || src.Height != 345)
{
    Console.Error.WriteLine($"Expected 600x345, got {src.Width}x{src.Height}");
    return 2;
}

// Keep original base tile 167 (left coast!) and put island on top with real alpha,
// same idea as symbols 154/155. Opaque full-tile grey was cutting the left continent.
const float Scale = 0.55f;
const float AnchorX = 0.56f; // nudged left again
const float AnchorY = 0.52f;
using var cut = PrepareTileWithTransparentOcean(src);
using var overlayBmp = ComposeScaledOnTransparentTile(cut, Scale, AnchorX, AnchorY);
var previewPath = Path.Combine(Path.GetTempPath(), "worldmap_overlay_preview.png");
SaveCheckerPreview(overlayBmp, previewPath);
Console.WriteLine($"Preview: {previewPath} (scale={Scale}, anchor=({AnchorX},{AnchorY}), transparent over original base)");
var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
var baseBackup = @"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient\clips\maps\0.swf.bak_20261009_231341";

foreach (var swfPath in targets)
{
    if (!File.Exists(swfPath))
    {
        Console.WriteLine($"SKIP missing: {swfPath}");
        continue;
    }

    var backup = swfPath + $".bak_overlay_{stamp}";
    File.Copy(swfPath, backup, overwrite: false);
    Console.WriteLine($"Backup: {backup}");

    var swf = SwfFile.Read(File.ReadAllBytes(swfPath));

    // Verify sprite 166 export
    if (!swf.ExportId(ExportName, out var expId) || expId != SpriteId)
    {
        Console.Error.WriteLine($"{Path.GetFileName(swfPath)}: expected export '{ExportName}' -> {SpriteId}, got {expId}");
        return 3;
    }

    var spriteIndex = swf.IndexOfCharacter(39, SpriteId);
    var baseShapeIndex = swf.IndexOfShape(BaseShapeId);
    if (spriteIndex < 0 || baseShapeIndex < 0)
    {
        Console.Error.WriteLine($"{Path.GetFileName(swfPath)}: missing sprite {SpriteId} or shape {BaseShapeId}");
        return 4;
    }

    var baseShapeTag = swf.Tags[baseShapeIndex];
    var bitmapIdOffset = FindBitmapIdOffset(swf, baseShapeTag.Data, bitmapIdHint: BaseBitmapId);
    if (bitmapIdOffset < 0)
    {
        Console.Error.WriteLine($"{Path.GetFileName(swfPath)}: cannot locate bitmap ref in shape {BaseShapeId}");
        return 4;
    }

    // If we already overlaid once, detect second PlaceObject and replace bitmap instead.
    var spriteData = swf.Tags[spriteIndex].Data;
    var placed = ParsePlacedChars(spriteData);
    Console.WriteLine($"Current sprite {SpriteId} places: [{string.Join(", ", placed)}]");

    ushort overlayBitmapId;
    ushort overlayShapeId;
    int insertAt;

    // Restore original coastal base (left land) from pre-edit backup.
    if (!RestoreBaseBitmapFromBackup(swf, BaseBitmapId, baseBackup))
    {
        Console.Error.WriteLine($"Could not restore base bitmap {BaseBitmapId} from backup");
        return 6;
    }

    if (placed.Count >= 2)
    {
        // Already has overlay: replace existing overlay bitmap referenced by second shape.
        overlayShapeId = placed[1];
        var shapeTag = swf.Tags.First(t => (t.Code is 2 or 22 or 32) && t.Data.Length >= 20 && BitConverter.ToUInt16(t.Data, 0) == overlayShapeId);
        var off = FindBitmapIdOffset(swf, shapeTag.Data, bitmapIdHint: 0);
        if (off < 0) { Console.Error.WriteLine("overlay shape has no bitmap ref"); return 5; }
        overlayBitmapId = BitConverter.ToUInt16(shapeTag.Data, off);
        var bmpIndex = swf.IndexOfCharacterAnyBitmap(overlayBitmapId);
        if (bmpIndex < 0)
        {
            Console.Error.WriteLine($"Overlay bitmap {overlayBitmapId} not found (off={off})");
            return 5;
        }

        swf.Tags[bmpIndex] = BuildDefineBitsLossless2(overlayBitmapId, overlayBmp);
        Console.WriteLine($"Replaced overlay bmp={overlayBitmapId} shape={overlayShapeId} (Lossless2 alpha over restored base)");
    }
    else
    {
        var nextId = (ushort)(swf.MaxCharacterId() + 1);
        overlayBitmapId = nextId;
        overlayShapeId = (ushort)(nextId + 1);

        var overlayShape = new SwfTag(
            baseShapeTag.Code,
            BuildBitmapShape(overlayShapeId, overlayBitmapId, baseShapeTag.Data, bitmapIdOffset));
        var overlayBitmap = BuildDefineBitsLossless2(overlayBitmapId, overlayBmp);
        var newSprite = BuildTwoLayerSprite(SpriteId, BaseShapeId, overlayShapeId, spriteData);

        // Insert bitmap+shape just before sprite 166 (same relative order as 154/155 before 151)
        insertAt = spriteIndex;
        swf.Tags.Insert(insertAt, overlayBitmap);
        swf.Tags.Insert(insertAt + 1, overlayShape);
        // sprite index shifted by +2
        swf.Tags[spriteIndex + 2] = new SwfTag(39, newSprite);

        Console.WriteLine($"Added overlay bmp={overlayBitmapId} shape={overlayShapeId} on sprite {SpriteId} (export {ExportName})");
        Console.WriteLine($"  depth1 -> shape {BaseShapeId} (base tile)");
        Console.WriteLine($"  depth2 -> shape {overlayShapeId} (isla)");
    }

    File.WriteAllBytes(swfPath, swf.Write(compress: true));
    Console.WriteLine($"OK {Path.GetFileName(swfPath)}");
}

Console.WriteLine("Done. Restart client / reopen worldmap. Export 2_-57 left untouched.");
return 0;

static List<ushort> ParsePlacedChars(byte[] spriteData)
{
    var list = new List<ushort>();
    var sp = 4;
    while (sp < spriteData.Length - 1)
    {
        var cl = BitConverter.ToUInt16(spriteData, sp); sp += 2;
        var tt = cl >> 6;
        var tl = cl & 0x3F;
        if (tl == 0x3F) { tl = BitConverter.ToInt32(spriteData, sp); sp += 4; }
        if (tt == 26 && tl >= 5 && (spriteData[sp] & 0x02) != 0)
            list.Add(BitConverter.ToUInt16(spriteData, sp + 3));
        if (tt == 0) break;
        sp += tl;
    }

    return list;
}

static byte[] BuildTwoLayerSprite(ushort spriteId, ushort baseShapeId, ushort overlayShapeId, byte[] originalSprite)
{
    // Clone first PlaceObject2 matrix bytes from original (depth1), then add depth2 with same matrix.
    // Original sprite 166: id, frames, PlaceObject2(len8), ShowFrame, End
    if (originalSprite.Length < 14)
        throw new InvalidOperationException("Sprite template too short.");

    using var ms = new MemoryStream();
    ms.Write(BitConverter.GetBytes(spriteId));
    ms.Write(BitConverter.GetBytes((ushort)1)); // frames

    // First place — keep original 8-byte PlaceObject2 data but ensure char = baseShapeId
    // header 88 06 already encodes type26 len8
    var place1Data = originalSprite.AsSpan(6, 8).ToArray(); // flags..matrix
    place1Data[0] = 0x0E;
    BitConverter.TryWriteBytes(place1Data.AsSpan(1, 2), (ushort)1); // depth
    BitConverter.TryWriteBytes(place1Data.AsSpan(3, 2), baseShapeId);
    ms.Write(BitConverter.GetBytes((ushort)((26 << 6) | 8)));
    ms.Write(place1Data);

    // Second place — same matrix fragment, depth 2, overlay shape
    var place2Data = (byte[])place1Data.Clone();
    BitConverter.TryWriteBytes(place2Data.AsSpan(1, 2), (ushort)2);
    BitConverter.TryWriteBytes(place2Data.AsSpan(3, 2), overlayShapeId);
    ms.Write(BitConverter.GetBytes((ushort)((26 << 6) | 8)));
    ms.Write(place2Data);

    // ShowFrame + End
    ms.Write(BitConverter.GetBytes((ushort)(1 << 6))); // ShowFrame len0
    ms.Write(BitConverter.GetBytes((ushort)0)); // End
    return ms.ToArray();
}

static bool RestoreBaseBitmapFromBackup(SwfFile swf, ushort bitmapId, string backupPath)
{
    if (!File.Exists(backupPath))
    {
        Console.WriteLine($"WARN: backup missing {backupPath}");
        return false;
    }

    var bak = SwfFile.Read(File.ReadAllBytes(backupPath));
    var bakTag = bak.Tags.FirstOrDefault(t =>
        (t.Code is 21 or 35 or 36) && t.Data.Length >= 2 && BitConverter.ToUInt16(t.Data, 0) == bitmapId);
    if (bakTag.Data is null)
    {
        Console.WriteLine($"WARN: bitmap {bitmapId} not in backup");
        return false;
    }

    var idx = swf.IndexOfCharacterAnyBitmap(bitmapId);
    if (idx < 0)
    {
        Console.WriteLine($"WARN: bitmap {bitmapId} not in target SWF");
        return false;
    }

    // Keep the character id; copy original tag code+payload from backup.
    var data = (byte[])bakTag.Data.Clone();
    BitConverter.TryWriteBytes(data.AsSpan(0, 2), bitmapId);
    swf.Tags[idx] = new SwfTag(bakTag.Code, data);
    Console.WriteLine($"Restored base bitmap {bitmapId} from backup ({NameOfBitmap(bakTag.Code)})");
    return true;
}

static string NameOfBitmap(int code) => code switch
{
    21 => "JPEG2",
    35 => "JPEG3",
    36 => "Lossless2",
    _ => $"T{code}"
};

static Bitmap ComposeScaledOnTransparentTile(Bitmap cutIsland, float scale, float anchorX, float anchorY)
{
    // Pre-clean halo on source, then nearest-neighbor scale (bicubic reintroduces white fringe).
    RemoveWhiteHalo(cutIsland, erodePasses: 3);
    using var cropped = CropOpaque(cutIsland, padding: 0);
    var tile = new Bitmap(600, 345, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(tile);
    g.Clear(Color.Transparent);
    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

    var dw = cropped.Width * scale;
    var dh = cropped.Height * scale;
    var x = (float)(600 * anchorX - dw / 2.0);
    var y = (float)(345 * anchorY - dh / 2.0);
    g.DrawImage(cropped, x, y, dw, dh);

    ScrubLowAlpha(tile, alphaThreshold: 200); // hard binary alpha
    RemoveWhiteHalo(tile, erodePasses: 2);
    ScrubLowAlpha(tile, alphaThreshold: 200);
    return tile;
}

/// <summary>Eat light/white anti-alias rim that reads as "relieve blanco" on dark sea.</summary>
static void RemoveWhiteHalo(Bitmap bmp, int erodePasses)
{
    var w = bmp.Width;
    var h = bmp.Height;
    var a = new byte[w * h];
    var rgb = new byte[w * h * 3];
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var c = bmp.GetPixel(x, y);
        var i = y * w + x;
        a[i] = c.A;
        rgb[i * 3] = c.R;
        rgb[i * 3 + 1] = c.G;
        rgb[i * 3 + 2] = c.B;
    }

    bool IsHalo(int i)
    {
        if (a[i] == 0) return false;
        var r = rgb[i * 3];
        var g = rgb[i * 3 + 1];
        var b = rgb[i * 3 + 2];
        var lum = (r * 3 + g * 4 + b) / 8;
        var warm = r - b; // sand coast is warm (R>>B); white halo is neutral/cool
        // True white / foam / AA against grey sea — NOT warm sand (#E1CCB9).
        if (lum >= 210 && warm < 28 && Math.Abs(r - g) < 30) return true;
        if (r >= 220 && g >= 220 && b >= 200 && warm < 35) return true;
        if (lum >= 190 && warm < 18 && Math.Abs(r - g) < 22) return true;
        return false;
    }

    bool TouchesClear(int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var nx = x + dx;
            var ny = y + dy;
            if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) return true;
            if (a[ny * w + nx] == 0) return true;
        }
        return false;
    }

    for (var pass = 0; pass < erodePasses; pass++)
    {
        var kill = new bool[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = y * w + x;
            if (a[i] == 0) continue;
            if (!TouchesClear(x, y)) continue;
            // Kill white halo; also soft alpha fringes (but keep warm sand).
            if (IsHalo(i) || (a[i] < 240 && (rgb[i * 3] - rgb[i * 3 + 2]) < 28))
                kill[i] = true;
        }
        for (var i = 0; i < kill.Length; i++)
            if (kill[i]) a[i] = 0;
    }

    // Also kill orphan bright speckles in the sea.
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var i = y * w + x;
        if (a[i] == 0 || !IsHalo(i)) continue;
        var clear = 0;
        for (var dy = -2; dy <= 2; dy++)
        for (var dx = -2; dx <= 2; dx++)
        {
            var nx = x + dx; var ny = y + dy;
            if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) { clear++; continue; }
            if (a[ny * w + nx] == 0) clear++;
        }
        if (clear >= 12) a[i] = 0;
    }

    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var i = y * w + x;
        if (a[i] == 0)
            bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
        else
            bmp.SetPixel(x, y, Color.FromArgb(255, rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]));
    }
}

static Bitmap CropOpaque(Bitmap src, int padding)
{
    var w = src.Width;
    var h = src.Height;
    var minX = w; var minY = h; var maxX = -1; var maxY = -1;
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        if (src.GetPixel(x, y).A < 16) continue;
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

    if (maxX < 0)
        return new Bitmap(1, 1, PixelFormat.Format32bppArgb);

    minX = Math.Max(0, minX - padding);
    minY = Math.Max(0, minY - padding);
    maxX = Math.Min(w - 1, maxX + padding);
    maxY = Math.Min(h - 1, maxY + padding);
    var cw = maxX - minX + 1;
    var ch = maxY - minY + 1;
    var dst = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(dst);
    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
    g.DrawImage(src, new Rectangle(0, 0, cw, ch), new Rectangle(minX, minY, cw, ch), GraphicsUnit.Pixel);
    return dst;
}

static void ScrubLowAlpha(Bitmap bmp, byte alphaThreshold)
{
    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var bd = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    try
    {
        var stride = bd.Stride;
        var row = new byte[Math.Abs(stride)];
        for (var y = 0; y < bmp.Height; y++)
        {
            Marshal.Copy(IntPtr.Add(bd.Scan0, y * stride), row, 0, row.Length);
            for (var x = 0; x < bmp.Width; x++)
            {
                var i = x * 4;
                if (row[i + 3] > alphaThreshold) continue;
                row[i] = 0; row[i + 1] = 0; row[i + 2] = 0; row[i + 3] = 0;
            }
            Marshal.Copy(row, 0, IntPtr.Add(bd.Scan0, y * stride), row.Length);
        }
    }
    finally { bmp.UnlockBits(bd); }
}

static void SaveCheckerPreview(Bitmap bmp, string path)
{
    using var prev = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(prev))
    {
        g.Clear(Color.FromArgb(255, 90, 90, 90));
        g.DrawImage(bmp, 0, 0);
    }
    prev.Save(path, ImageFormat.Png);
}

static SwfTag BuildDefineBitsJpeg2(ushort id, Bitmap bmp)
{
    using var rgbBmp = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb);
    using (var g = Graphics.FromImage(rgbBmp))
        g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);

    byte[] jpeg;
    using (var ms = new MemoryStream())
    {
        var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
        rgbBmp.Save(ms, enc, ep);
        jpeg = ms.ToArray();
    }

    // Dummy empty JPEG prefix used by Dofus worldmap bitmaps.
    using var tag = new MemoryStream();
    tag.Write(BitConverter.GetBytes(id));
    tag.WriteByte(0xFF); tag.WriteByte(0xD8); tag.WriteByte(0xFF); tag.WriteByte(0xD9);
    tag.Write(jpeg);
    return new SwfTag(21, tag.ToArray()); // DefineBitsJPEG2
}

static byte[] ZlibCompress(byte[] raw)
{
    using var ms = new MemoryStream();
    using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        zs.Write(raw);
    return ms.ToArray();
}

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
                argb[oi] = row[bi + 3];
                argb[oi + 1] = row[bi + 2];
                argb[oi + 2] = row[bi + 1];
                argb[oi + 3] = row[bi];
            }
        }
    }
    finally { bmp.UnlockBits(bd); }

    using var ms = new MemoryStream();
    ms.Write(BitConverter.GetBytes(id));
    ms.WriteByte(5);
    ms.Write(BitConverter.GetBytes((ushort)w));
    ms.Write(BitConverter.GetBytes((ushort)h));
    using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        zs.Write(argb);
    return new SwfTag(36, ms.ToArray());
}

static byte[] BuildBitmapShape(ushort shapeId, ushort bitmapId, byte[] template, int bitmapIdOffset)
{
    var data = (byte[])template.Clone();
    BitConverter.TryWriteBytes(data.AsSpan(0, 2), shapeId);
    BitConverter.TryWriteBytes(data.AsSpan(bitmapIdOffset, 2), bitmapId);
    return data;
}

static int FindBitmapIdOffset(SwfFile swf, byte[] shapeData, ushort bitmapIdHint)
{
    if (bitmapIdHint != 0)
    {
        for (var i = 2; i < shapeData.Length - 1; i++)
            if (BitConverter.ToUInt16(shapeData, i) == bitmapIdHint)
                return i;
    }

    // Prefer classic fill-style slots used by these tiles.
    foreach (var i in new[] { 18, 17, 16, 19, 20 })
    {
        if (i + 1 >= shapeData.Length) continue;
        var v = BitConverter.ToUInt16(shapeData, i);
        if (swf.IndexOfCharacterAnyBitmap(v) >= 0) return i;
    }

    for (var i = 2; i < shapeData.Length - 1; i++)
    {
        var v = BitConverter.ToUInt16(shapeData, i);
        if (swf.IndexOfCharacterAnyBitmap(v) >= 0) return i;
    }

    return -1;
}

static Bitmap PrepareTileWithTransparentOcean(Bitmap src)
{
    var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
        g.DrawImage(src, 0, 0, src.Width, src.Height);

    var water = AverageCornerWater(bmp);
    var w = bmp.Width;
    var h = bmp.Height;
    var mark = new bool[w * h];
    var q = new Queue<int>();

    bool IsOceanLike(Color c)
    {
        // Flat sea grey-blue around sample.
        var dr = Math.Abs(c.R - water.R);
        var dg = Math.Abs(c.G - water.G);
        var db = Math.Abs(c.B - water.B);
        if (dr <= 42 && dg <= 42 && db <= 42) return true;
        if (dr + dg + db <= 70 && c.B + 8 >= c.R && c.G + 8 >= c.R) return true;
        // Wave dashes / foam (near-white, low saturation).
        if (c.R >= 175 && c.G >= 175 && c.B >= 175 &&
            Math.Abs(c.R - c.G) < 30 && Math.Abs(c.G - c.B) < 30)
            return true;
        // Light fringe / anti-alias between sea and land (desaturated mid-light).
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        if (max - min <= 28 && max >= 140 && max <= 210 && c.B + 5 >= c.R)
            return true;
        return false;
    }

    void Seed(int x, int y)
    {
        if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
        var i = y * w + x;
        if (mark[i]) return;
        if (!IsOceanLike(bmp.GetPixel(x, y))) return;
        mark[i] = true;
        q.Enqueue(i);
    }

    Seed(0, 0); Seed(w - 1, 0); Seed(0, h - 1); Seed(w - 1, h - 1);
    // Extra seeds along borders (closed lagoons / cut corners).
    for (var x = 0; x < w; x += 8) { Seed(x, 0); Seed(x, h - 1); }
    for (var y = 0; y < h; y += 8) { Seed(0, y); Seed(w - 1, y); }

    while (q.Count > 0)
    {
        var i = q.Dequeue();
        var x = i % w;
        var y = i / w;
        Seed(x + 1, y); Seed(x - 1, y); Seed(x, y + 1); Seed(x, y - 1);
    }

    // Kill leftover wave dashes anywhere near already-cleared sea (wider neighborhood).
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var i = y * w + x;
        if (mark[i]) continue;
        var c = bmp.GetPixel(x, y);
        if (!(c.R >= 170 && c.G >= 170 && c.B >= 170 &&
              Math.Abs(c.R - c.G) < 35 && Math.Abs(c.G - c.B) < 35))
            continue;
        if (HasMarkedNeighbor(mark, w, h, x, y, radius: 4))
            mark[i] = true;
    }

    // Dilate transparency into fringe (removes "relieve"/halo on zoom).
    for (var pass = 0; pass < 6; pass++)
    {
        var grow = new bool[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = y * w + x;
            if (mark[i]) continue;
            if (!HasMarkedNeighbor(mark, w, h, x, y, radius: 1)) continue;
            var c = bmp.GetPixel(x, y);
            // Eat sea / pale AA / bright rim / any light edge next to sea.
            if (IsOceanLike(c) || IsFringe(c) || IsBrightRim(c) || IsPaleEdge(c))
                grow[i] = true;
        }
        for (var i = 0; i < grow.Length; i++)
            if (grow[i]) mark[i] = true;
    }

    // Final despeckle: any near-white pixel still opaque → clear if mostly sea around it.
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var i = y * w + x;
        if (mark[i]) continue;
        var c = bmp.GetPixel(x, y);
        if (!IsBrightRim(c) && !(c.R >= 190 && c.G >= 190 && c.B >= 190)) continue;
        var sea = 0;
        var total = 0;
        for (var dy = -3; dy <= 3; dy++)
        for (var dx = -3; dx <= 3; dx++)
        {
            var nx = x + dx; var ny = y + dy;
            if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
            total++;
            if (mark[ny * w + nx]) sea++;
        }
        if (total > 0 && sea * 10 >= total * 4) // >=40% sea around
            mark[i] = true;
    }

    // Apply alpha; zero RGB on clear pixels to avoid Flash white-fringe filtering.
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        if (!mark[y * w + x]) continue;
        bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
    }

    return bmp;
}

static Color AverageCornerWater(Bitmap bmp)
{
    var pts = new[]
    {
        (0, 0), (bmp.Width - 1, 0), (0, bmp.Height - 1), (bmp.Width - 1, bmp.Height - 1),
        (8, 8), (bmp.Width - 9, 8), (8, bmp.Height - 9), (bmp.Width - 9, bmp.Height - 9),
    };
    long r = 0, g = 0, b = 0;
    foreach (var (x, y) in pts)
    {
        var c = bmp.GetPixel(x, y);
        r += c.R; g += c.G; b += c.B;
    }
    return Color.FromArgb((int)(r / pts.Length), (int)(g / pts.Length), (int)(b / pts.Length));
}

static bool IsFringe(Color c)
{
    // Pale desaturated rim left by AA against sea.
    var max = Math.Max(c.R, Math.Max(c.G, c.B));
    var min = Math.Min(c.R, Math.Min(c.G, c.B));
    if (max < 120) return false;
    if (max - min > 45) return false; // keep saturated sand/brown
    // Land sand is warmer (R >> B). Fringe/sea AA stays cooler/neutral.
    return c.R <= c.B + 22;
}

static bool IsBrightRim(Color c) =>
    c.R >= 185 && c.G >= 180 && c.B >= 160 &&
    Math.Abs(c.R - c.G) < 40 && c.R + c.G + c.B >= 540;

static bool IsPaleEdge(Color c)
{
    // Light sand AA against sea — warmer than fringe but still halo-like.
    var lum = (c.R * 3 + c.G * 4 + c.B) / 8;
    if (lum < 195) return false;
    if (c.R < 200) return false;
    // Keep deep brown props (low lum / high contrast stripes).
    return c.B >= 140 || (c.G >= 185 && c.R >= 210);
}

static bool HasMarkedNeighbor(bool[] mark, int w, int h, int x, int y, int radius)
{
    for (var dy = -radius; dy <= radius; dy++)
    for (var dx = -radius; dx <= radius; dx++)
    {
        var nx = x + dx;
        var ny = y + dy;
        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
        if (mark[ny * w + nx]) return true;
    }
    return false;
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
            if (length == 0x3F) { length = BitConverter.ToInt32(body, o + 2); hdr = 6; }
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

    public bool ExportId(string name, out ushort id)
    {
        id = 0;
        foreach (var tag in Tags)
        {
            if (tag.Code != 56 || tag.Data.Length < 2) continue;
            var count = BitConverter.ToUInt16(tag.Data, 0);
            var p = 2;
            for (var i = 0; i < count; i++)
            {
                var cid = BitConverter.ToUInt16(tag.Data, p); p += 2;
                var end = p;
                while (end < tag.Data.Length && tag.Data[end] != 0) end++;
                var n = Encoding.ASCII.GetString(tag.Data, p, end - p);
                p = end + 1;
                if (string.Equals(n, name, StringComparison.Ordinal))
                {
                    id = cid;
                    return true;
                }
            }
        }

        return false;
    }

    public int IndexOfCharacter(int code, ushort id)
    {
        for (var i = 0; i < Tags.Count; i++)
        {
            var t = Tags[i];
            if (t.Code == code && t.Data.Length >= 2 && BitConverter.ToUInt16(t.Data, 0) == id)
                return i;
        }

        return -1;
    }

    public int IndexOfShape(ushort id)
    {
        for (var i = 0; i < Tags.Count; i++)
        {
            var t = Tags[i];
            if ((t.Code is 2 or 22 or 32) && t.Data.Length >= 2 && BitConverter.ToUInt16(t.Data, 0) == id)
                return i;
        }

        return -1;
    }

    public int IndexOfCharacterAnyBitmap(ushort id)
    {
        for (var i = 0; i < Tags.Count; i++)
        {
            var t = Tags[i];
            if ((t.Code is 21 or 35 or 36) && t.Data.Length >= 2 && BitConverter.ToUInt16(t.Data, 0) == id)
                return i;
        }

        return -1;
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
