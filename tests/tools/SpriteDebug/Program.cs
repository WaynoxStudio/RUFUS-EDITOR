using System.Drawing;
using System.Drawing.Imaging;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

var clips = args.Length > 0 ? args[0]
    : @"C:\Users\rubez\Desktop\RUFUS RETRO\resources\app\retroclient\clips";
var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "sprites");
outDir = Path.GetFullPath(outDir);
Directory.CreateDirectory(outDir);

var ids = new[] { 30, 71, 120, 1245, 9073 };
Console.WriteLine("=== SPRITE DEBUG 3E.1 staticR ===");
Console.WriteLine($"Clips: {clips}");
Console.WriteLine($"Out:   {outDir}");

static SwfSpritePick? PickOldHeuristic(SwfMovie movie)
{
    SwfSpriteDefinition? best = null;
    var bestScore = 0L;
    foreach (var sprite in movie.Sprites.Values)
    {
        if (sprite.PayloadBytes < 2000) continue;
        if (sprite.FrameCount is < 24 or > 120) continue;
        var score = (long)sprite.PayloadBytes * sprite.FrameCount;
        if (score > bestScore)
        {
            bestScore = score;
            best = sprite;
        }
    }

    return best is null
        ? null
        : new SwfSpritePick(best.CharacterId, 0, "OLD walk-cycle heuristic", null);
}

foreach (var gfxId in ids)
{
    var swfPath = Path.Combine(clips, "sprites", gfxId + ".swf");
    if (!File.Exists(swfPath))
    {
        Console.WriteLine($"\nGFX {gfxId}: SWF MISSING");
        continue;
    }

    var bytes = File.ReadAllBytes(swfPath);
    var movie = SwfMovieParser.Parse(bytes);
    SwfSpriteSelection.TryPickExactExport(movie, "staticR", out var staticRPick);
    var pick = SwfSpriteSelection.SelectThumbnail(movie);
    var composer = new SwfTimelineComposer(movie);
    var diag = composer.Analyze(pick.SpriteId, pick.FrameIndex);

    Console.WriteLine($"\n--- GFX {gfxId} ---");
    Console.WriteLine($"staticR: exists={staticRPick.SpriteId > 0} charId={staticRPick.SpriteId} linkage={staticRPick.LinkageName} reason={staticRPick.Reason}");
    Console.WriteLine($"Selected: sprite={pick.SpriteId} frame={pick.FrameIndex} linkage={pick.LinkageName} reason={pick.Reason}");
    Console.WriteLine($"Analyze: depths={diag.ActiveDepths} shapes={diag.ShapesDrawn} nested={diag.NestedSprites} bitmaps={diag.BitmapsDrawn}");
    Console.WriteLine($"Bounds visible: {diag.VisibleBounds.Width:F0}x{diag.VisibleBounds.Height:F0}");

    var oldPick = PickOldHeuristic(movie);
    var compareCell = 128;
    using var compare = new Bitmap(compareCell * 2 + 8, compareCell + 24, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(compare))
    {
        g.Clear(Color.FromArgb(32, 32, 32));
        if (oldPick is { } old)
        {
            try
            {
                using var oldBmp = SwfSpriteThumbnailRenderer.RasterizeMovieFrame(bytes, old.SpriteId, old.FrameIndex, compareCell, gfxId);
                g.DrawImage(oldBmp, 0, 0, compareCell, compareCell);
            }
            catch
            {
                g.FillRectangle(Brushes.DarkRed, 0, 0, compareCell, compareCell);
            }

            g.DrawString($"OLD {old.SpriteId}", SystemFonts.DefaultFont, Brushes.White, 2, compareCell + 4);
        }

        try
        {
            using var newBmp = SwfSpriteThumbnailRenderer.RasterizeToBitmap(bytes, compareCell, gfxId, out _);
            g.DrawImage(newBmp, compareCell + 8, 0, compareCell, compareCell);
        }
        catch
        {
            g.FillRectangle(Brushes.DarkRed, compareCell + 8, 0, compareCell, compareCell);
        }

        g.DrawString($"staticR {pick.SpriteId}", SystemFonts.DefaultFont, Brushes.White, compareCell + 10, compareCell + 4);
    }

    var comparePath = Path.Combine(outDir, $"{gfxId}_compare_old_vs_staticR.png");
    compare.Save(comparePath, ImageFormat.Png);
    Console.WriteLine($"Compare: {comparePath}");

    var thumb96 = SwfSpriteThumbnailRenderer.RasterizeToPng(bytes, 96, gfxId);
    var thumbPath = Path.Combine(outDir, $"{gfxId}_thumb96_v3.png");
    File.WriteAllBytes(thumbPath, thumb96);
    Console.WriteLine($"Thumb96 v3: {thumbPath} ({thumb96.Length} bytes)");
}

Console.WriteLine("\n=== NPC PREVIEW SERVICE (v3 cache) ===");
var library = args.Length > 1 ? args[1] : @"c:\Users\rubez\Desktop\RUFUS EDITOR\Library";
var preview = NpcGfxPreviewService.Shared;
preview.Configure(clips, library);
foreach (var gfxId in ids)
{
    preview.SpriteCache.ClearFailed(gfxId);
    var (png, info) = preview.GetOrCreatePngWithInfoAsync(gfxId).GetAwaiter().GetResult();
    Console.WriteLine(
        $"GFX {gfxId}: source={info.Source} renderer={info.Renderer} sprite={info.SpriteId} frame={info.FrameIndex} " +
        $"reason={info.SelectionReason} fallback={info.UsedArtworkFallback} png={(png?.Length.ToString() ?? "null")} cache={info.CachePath}");
}
