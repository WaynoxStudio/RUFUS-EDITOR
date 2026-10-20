using System.Drawing;
using System.Drawing.Imaging;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class NpcGfxPreviewPrepTests
{
    [Fact]
    public void Classify_9059_band_is_Ok_1245_band_is_Failed()
    {
        // Calibrated from real FFDec exports (opaque / visBounds).
        Assert.Equal(
            NpcGfxPreviewPrepStatus.Ok,
            NpcGfxPngContentValidator.Classify(9407, 120, 102, 120 * 102));

        Assert.Equal(
            NpcGfxPreviewPrepStatus.Failed,
            NpcGfxPngContentValidator.Classify(93, 16, 8, 16 * 8));
    }

    [Fact]
    public void Classify_ambiguous_is_Review()
    {
        Assert.Equal(
            NpcGfxPreviewPrepStatus.Review,
            NpcGfxPngContentValidator.Classify(400, 50, 50, 2500));
    }

    [Fact]
    public void ValidateBytes_rejects_empty_and_invalid()
    {
        var empty = NpcGfxPngContentValidator.ValidateBytes(Array.Empty<byte>());
        Assert.False(empty.Decoded);
        Assert.Equal(NpcGfxPreviewPrepStatus.Failed, empty.ContentStatus);

        var bad = NpcGfxPngContentValidator.ValidateBytes(new byte[] { 1, 2, 3, 4 });
        Assert.False(bad.Decoded);
        Assert.Equal(NpcGfxPreviewPrepStatus.Failed, bad.ContentStatus);
    }

    [Fact]
    public void ValidateFile_detects_nearly_empty_png()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "emptyish.png");
        WriteSparsePng(path, 200, 200, opaquePixels: 40);

        var v = NpcGfxPngContentValidator.ValidateFile(path);
        Assert.True(v.Decoded);
        Assert.Equal(NpcGfxPreviewPrepStatus.Failed, v.ContentStatus);
    }

    [Fact]
    public void ValidateFile_accepts_dense_content_png()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "good.png");
        WriteFilledRectPng(path, 200, 200, new Rectangle(20, 20, 120, 120));

        var v = NpcGfxPngContentValidator.ValidateFile(path);
        Assert.True(v.Decoded);
        Assert.Equal(NpcGfxPreviewPrepStatus.Ok, v.ContentStatus);
        Assert.True(v.OpaquePixelCount >= NpcGfxPngContentValidator.OkMinOpaquePixels);
    }

    [Fact]
    public void ProcessOne_ffdec_path_missing_fails_clearly()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 9059);
        var opts = new NpcGfxPreviewPrepOptions
        {
            FfdecCliPath = Path.Combine(dir.Path, "missing-ffdec-cli.exe"),
            ClipsRoot = clips,
            StagingRoot = Path.Combine(dir.Path, "staging"),
            GfxIds = new[] { 9059 },
        };

        var entry = new NpcGfxPreviewPrepService(new FakeFfdec()).ProcessOne(opts, 9059);
        Assert.Equal(NpcGfxPreviewPrepStatus.Failed, entry.Status);
        Assert.Contains("ffdec-cli", entry.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessOne_no_artwork_status()
    {
        using var dir = new TempDir();
        var clips = CreateClipsRoot(dir.Path);
        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"));
        File.WriteAllText(opts.FfdecCliPath, "x");

        var entry = new NpcGfxPreviewPrepService(new FakeFfdec()).ProcessOne(opts, 999001);
        Assert.Equal(NpcGfxPreviewPrepStatus.NoArtwork, entry.Status);
    }

    [Fact]
    public void ProcessOne_manual_exists_not_overwritten()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 71);
        var lib = Path.Combine(dir.Path, "Library");
        var manual = Path.Combine(lib, "Visuals", "Mobs", "71.png");
        Directory.CreateDirectory(Path.GetDirectoryName(manual)!);
        WriteFilledRectPng(manual, 64, 64, new Rectangle(0, 0, 64, 64));

        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"), library: lib);
        File.WriteAllText(opts.FfdecCliPath, "x");

        var fake = new FakeFfdec { ShouldThrowIfCalled = true };
        var entry = new NpcGfxPreviewPrepService(fake).ProcessOne(opts, 71);
        Assert.Equal(NpcGfxPreviewPrepStatus.ManualExists, entry.Status);
        Assert.False(fake.WasCalled);
    }

    [Fact]
    public void ProcessOne_timeout_is_failed()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 30);
        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"));
        File.WriteAllText(opts.FfdecCliPath, "x");

        var entry = new NpcGfxPreviewPrepService(new FakeFfdec { TimedOut = true }).ProcessOne(opts, 30);
        Assert.Equal(NpcGfxPreviewPrepStatus.Failed, entry.Status);
        Assert.True(entry.TimedOut);
        Assert.Equal("TIMEOUT", entry.Reason);
    }

    [Fact]
    public void ProcessOne_ffdec_exit_nonzero_failed()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 30);
        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"));
        File.WriteAllText(opts.FfdecCliPath, "x");

        var entry = new NpcGfxPreviewPrepService(new FakeFfdec { ExitCode = 7 }).ProcessOne(opts, 30);
        Assert.Equal(NpcGfxPreviewPrepStatus.Failed, entry.Status);
        Assert.Contains("exit 7", entry.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void PromoteOkFromBatch_copies_only_ok_and_never_overwrites()
    {
        using var dir = new TempDir();
        var batch = Path.Combine(dir.Path, "batch");
        var native = Path.Combine(batch, "native");
        var lib = Path.Combine(dir.Path, "Library");
        Directory.CreateDirectory(native);
        Directory.CreateDirectory(Path.Combine(lib, "Visuals", "Mobs"));

        WriteFilledRectPng(Path.Combine(native, "30.png"), 256, 256, new Rectangle(10, 10, 100, 100));
        WriteFilledRectPng(Path.Combine(native, "71.png"), 256, 256, new Rectangle(10, 10, 80, 80));
        WriteFilledRectPng(Path.Combine(native, "1515.png"), 256, 256, new Rectangle(10, 10, 40, 40));
        // existing manual
        WriteFilledRectPng(Path.Combine(lib, "Visuals", "Mobs", "71.png"), 64, 64, new Rectangle(0, 0, 64, 64));
        var before71 = File.ReadAllBytes(Path.Combine(lib, "Visuals", "Mobs", "71.png"));

        var manifest = """
            {
              "Entries": [
                { "GfxId": 30, "Status": "OK", "OutputPng": "native/30.png" },
                { "GfxId": 71, "Status": "OK", "OutputPng": "native/71.png" },
                { "GfxId": 1515, "Status": "REVIEW", "OutputPng": "native/1515.png" },
                { "GfxId": 1168, "Status": "FAILED" },
                { "GfxId": 1200, "Status": "NOARTWORK" }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(batch, "manifest-native.json"), manifest);

        var result = NpcGfxPreviewPrepService.PromoteOkFromBatch(batch, lib);
        Assert.Equal(2, result.OkDetected);
        Assert.Equal(new[] { 30 }, result.Created.ToArray());
        Assert.Equal(new[] { 71 }, result.SkippedManual.ToArray());
        Assert.Equal(3, result.SkippedStatus.Count);
        Assert.Equal(0, result.ManualOverwrites);
        Assert.True(File.Exists(Path.Combine(lib, "Visuals", "Mobs", "30.png")));
        Assert.False(File.Exists(Path.Combine(lib, "Visuals", "Mobs", "1515.png")));
        Assert.Equal(before71, File.ReadAllBytes(Path.Combine(lib, "Visuals", "Mobs", "71.png")));
    }

    [Fact]
    public void ProcessOne_exit_ok_but_empty_png_not_silently_accepted()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 1245);
        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"));
        File.WriteAllText(opts.FfdecCliPath, "x");

        var fake = new FakeFfdec
        {
            WritePng = outDir => WriteSparsePng(Path.Combine(outDir, "1.png"), 200, 200, opaquePixels: 50),
        };
        var entry = new NpcGfxPreviewPrepService(fake).ProcessOne(opts, 1245);
        Assert.Equal(NpcGfxPreviewPrepStatus.Failed, entry.Status);
        Assert.Contains("vacío", entry.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessOne_exit_ok_good_png_is_Ok()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 9059);
        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"));
        File.WriteAllText(opts.FfdecCliPath, "x");

        var fake = new FakeFfdec
        {
            WritePng = outDir => WriteFilledRectPng(
                Path.Combine(outDir, "1.png"), 550, 400, new Rectangle(100, 100, 120, 120)),
        };
        var entry = new NpcGfxPreviewPrepService(fake).ProcessOne(opts, 9059);
        Assert.Equal(NpcGfxPreviewPrepStatus.Ok, entry.Status);
        Assert.True(File.Exists(entry.OutputPng));
        Assert.True(entry.Normalized);
        Assert.Equal(NpcGfxPreviewCropNormalizer.CanvasSize, entry.Width);
        Assert.Equal(NpcGfxPreviewCropNormalizer.CanvasSize, entry.Height);
        Assert.Equal(550, entry.SourceWidth);
        Assert.Equal(400, entry.SourceHeight);
    }

    [Fact]
    public void ProcessOne_review_band_not_promoted_as_Ok()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 120);
        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"));
        File.WriteAllText(opts.FfdecCliPath, "x");

        // Mid band → REVIEW (25×25=625 opaque: above fail floor, below OK floor)
        var fake = new FakeFfdec
        {
            WritePng = outDir => WriteFilledRectPng(
                Path.Combine(outDir, "1.png"), 100, 100, new Rectangle(10, 10, 25, 25)),
        };
        var entry = new NpcGfxPreviewPrepService(fake).ProcessOne(opts, 120);
        Assert.Equal(NpcGfxPreviewPrepStatus.Review, entry.Status);
    }

    [Fact]
    public void Manifest_written_with_statuses()
    {
        using var dir = new TempDir();
        var entries = new[]
        {
            new NpcGfxPreviewPrepEntry
            {
                GfxId = 9059,
                Status = NpcGfxPreviewPrepStatus.Ok,
                Width = 10,
                Height = 10,
                Reason = "ok",
                SourceSwf = @"C:\Users\someone\clips\artworks\big\9059.swf",
                OutputPng = @"C:\Users\someone\staging\native\9059.png",
            },
            new NpcGfxPreviewPrepEntry
            {
                GfxId = 1245,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "empty",
            },
        };
        var summary = NpcGfxPreviewPrepService.BuildSummary(entries, dir.Path, confirmedGfxCount: 250);
        var path = Path.Combine(dir.Path, "manifest.json");
        NpcGfxPreviewPrepService.WriteManifest(summary, path);
        var json = File.ReadAllText(path);
        Assert.Contains("\"OK\"", json, StringComparison.Ordinal);
        Assert.Contains("\"FAILED\"", json, StringComparison.Ordinal);
        Assert.Contains("9059", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\someone", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Promote_only_copies_when_dest_missing()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 9059);
        var lib = Path.Combine(dir.Path, "Library");
        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"), library: lib);
        opts = new NpcGfxPreviewPrepOptions
        {
            FfdecCliPath = opts.FfdecCliPath,
            ClipsRoot = opts.ClipsRoot,
            StagingRoot = opts.StagingRoot,
            LibraryRoot = lib,
            GfxIds = new[] { 9059 },
            PromoteOkToLibrary = true,
        };
        File.WriteAllText(opts.FfdecCliPath, "x");

        var fake = new FakeFfdec
        {
            WritePng = outDir => WriteFilledRectPng(
                Path.Combine(outDir, "1.png"), 200, 200, new Rectangle(10, 10, 150, 150)),
        };
        var entry = new NpcGfxPreviewPrepService(fake).ProcessOne(opts, 9059);
        Assert.Equal(NpcGfxPreviewPrepStatus.Ok, entry.Status);
        var dest = NpcGfxPreviewPrepService.ResolveManualPng(lib, 9059)!;
        Assert.True(File.Exists(dest));

        // Second run with manual present → MANUAL_EXISTS, no overwrite
        var before = File.GetLastWriteTimeUtc(dest);
        Thread.Sleep(20);
        var again = new NpcGfxPreviewPrepService(new FakeFfdec { ShouldThrowIfCalled = true }).ProcessOne(opts, 9059);
        Assert.Equal(NpcGfxPreviewPrepStatus.ManualExists, again.Status);
        Assert.Equal(before, File.GetLastWriteTimeUtc(dest));
    }

    [Fact]
    public void CropNormalizer_crops_external_transparency_and_centers()
    {
        using var src = new Bitmap(200, 200, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(src))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(Brushes.Red, new Rectangle(10, 20, 40, 60)); // top-left content
        }

        var result = NpcGfxPreviewCropNormalizer.NormalizeToPngBytes(src);
        Assert.True(result.HasContent);
        Assert.Equal(10, result.BoundX);
        Assert.Equal(20, result.BoundY);
        Assert.Equal(40, result.BoundWidth);
        Assert.Equal(60, result.BoundHeight);
        Assert.Equal(NpcGfxPreviewCropNormalizer.CanvasSize, result.CanvasWidth);

        using var outBmp = new Bitmap(new MemoryStream(result.PngBytes));
        Assert.Equal(256, outBmp.Width);
        Assert.Equal(256, outBmp.Height);

        var bounds = NpcGfxPreviewCropNormalizer.MeasureVisibleBounds(outBmp);
        Assert.True(bounds.HasContent);
        // Centered: midpoint near canvas center
        var cx = bounds.X + bounds.Width / 2.0;
        var cy = bounds.Y + bounds.Height / 2.0;
        Assert.InRange(cx, 256 * 0.4, 256 * 0.6);
        Assert.InRange(cy, 256 * 0.4, 256 * 0.6);

        // Aspect ~ 40:60 = 2:3 preserved
        var aspect = bounds.Width / (double)bounds.Height;
        Assert.InRange(aspect, 40.0 / 60.0 - 0.08, 40.0 / 60.0 + 0.08);
    }

    [Fact]
    public void CropNormalizer_preserves_internal_alpha_holes()
    {
        using var src = new Bitmap(80, 80, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(src))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(Brushes.Blue, new Rectangle(0, 0, 80, 80));
            g.FillRectangle(Brushes.Transparent, new Rectangle(30, 30, 20, 20)); // hole
            // Clear with Transparent may not punch hole under SourceOver — force pixels
        }

        for (var y = 30; y < 50; y++)
        for (var x = 30; x < 50; x++)
            src.SetPixel(x, y, Color.Transparent);

        var result = NpcGfxPreviewCropNormalizer.NormalizeToPngBytes(src);
        using var outBmp = new Bitmap(new MemoryStream(result.PngBytes));
        // Center pixel of canvas should remain transparent (hole survives roughly)
        var mid = outBmp.GetPixel(outBmp.Width / 2, outBmp.Height / 2);
        Assert.True(mid.A < NpcGfxPngContentValidator.AlphaThreshold);
        // A ring around center should be opaque
        var ring = outBmp.GetPixel(outBmp.Width / 2, outBmp.Height / 2 - 40);
        Assert.True(ring.A >= NpcGfxPngContentValidator.AlphaThreshold);
    }

    [Fact]
    public void CropNormalizer_fully_transparent_has_no_content()
    {
        using var src = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(src))
            g.Clear(Color.Transparent);

        var result = NpcGfxPreviewCropNormalizer.NormalizeToPngBytes(src);
        Assert.False(result.HasContent);
    }

    [Fact]
    public void ProcessOne_tiny_content_stays_Failed_after_normalize()
    {
        using var dir = new TempDir();
        var clips = CreateClipsWithArtwork(dir.Path, 71);
        var opts = BaseOpts(dir, clips, ffdec: Path.Combine(dir.Path, "fake.exe"));
        File.WriteAllText(opts.FfdecCliPath, "x");

        // Tiny blob like GFX 71 / 1245 — would look bigger after normalize but must stay Failed
        var fake = new FakeFfdec
        {
            WritePng = outDir => WriteSparsePng(Path.Combine(outDir, "1.png"), 550, 400, opaquePixels: 86),
        };
        var entry = new NpcGfxPreviewPrepService(fake).ProcessOne(opts, 71);
        Assert.Equal(NpcGfxPreviewPrepStatus.Failed, entry.Status);
        Assert.True(entry.OpaquePixelCount <= NpcGfxPngContentValidator.FailMaxOpaquePixels);
    }

    [Fact]
    public void ExportTarget_9059_selects_composite_sprite_when_clips_available()
    {
        var swf = TryRealArtwork(9059);
        if (swf is null)
        {
            // Offline CI without Retro clips — skip without failing the suite.
            return;
        }

        var sel = SwfArtworkExportTargetSelector.Select(File.ReadAllBytes(swf));
        Assert.Equal(SwfArtworkExportTargetSelector.ExportKind.Sprite, sel.Kind);
        Assert.Equal(23, sel.SpriteCharacterId);
        Assert.True(sel.NestedPlaceCount >= SwfArtworkExportTargetSelector.MinNestedPlacesForComposite);
        Assert.Equal(1, sel.ContentRootSpriteCount);
    }

    [Fact]
    public void ExportTarget_30_selects_full_root_svg_multi_symbol_root()
    {
        var swf = TryRealArtwork(30);
        if (swf is null)
            return;

        var sel = SwfArtworkExportTargetSelector.Select(File.ReadAllBytes(swf));
        Assert.Equal(SwfArtworkExportTargetSelector.ExportKind.FullRootSvg, sel.Kind);
        Assert.True(sel.RootPlaceCount > 1);
    }

    [Fact]
    public void ExportTarget_120_prefers_full_root_over_false_dominant_sprite()
    {
        var swf = TryRealArtwork(120);
        if (swf is null)
            return;

        var sel = SwfArtworkExportTargetSelector.Select(File.ReadAllBytes(swf));
        Assert.Equal(SwfArtworkExportTargetSelector.ExportKind.FullRootSvg, sel.Kind);
        Assert.True(sel.ContentRootSpriteCount > 1 || sel.RootPlaceCount > 1);
        Assert.NotEqual(SwfArtworkExportTargetSelector.ExportKind.Sprite, sel.Kind);
    }

    [Fact]
    public void ExportTarget_9073_prefers_full_root_over_clipped_frame()
    {
        var swf = TryRealArtwork(9073);
        if (swf is null)
            return;

        var sel = SwfArtworkExportTargetSelector.Select(File.ReadAllBytes(swf));
        Assert.Equal(SwfArtworkExportTargetSelector.ExportKind.FullRootSvg, sel.Kind);
        Assert.True(sel.HasOffStagePlace || sel.RootShapeCount >= 1);
        Assert.NotEqual(SwfArtworkExportTargetSelector.ExportKind.Frame, sel.Kind);
    }

    [Fact]
    public void ExportTarget_prefers_sprite_over_full_root_when_wrapper_exists()
    {
        var swf = TryRealArtwork(9059);
        if (swf is null)
            return;

        var sel = SwfArtworkExportTargetSelector.Select(File.ReadAllBytes(swf));
        Assert.Equal(SwfArtworkExportTargetSelector.ExportKind.Sprite, sel.Kind);
        Assert.NotEqual(SwfArtworkExportTargetSelector.ExportKind.FullRootSvg, sel.Kind);
    }

    [Fact]
    public void SvgBounds_expands_negative_coords_beyond_stage()
    {
        const string svg = """
            <?xml version="1.0" encoding="utf-8"?>
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                 width="550px" height="400px" viewBox="0 0 550 400">
              <defs><g id="shape1"><path d="M0 0 L10 0 10 10 0 10 Z" fill="#fff"/></g></defs>
              <g>
                <use id="a" width="100" height="80" transform="matrix(1,0,0,1,-40,-120)" xlink:href="#shape1"/>
                <use id="b" width="50" height="50" transform="matrix(1,0,0,1,200,300)" xlink:href="#shape1"/>
              </g>
            </svg>
            """;

        var bounds = SvgFrameBoundsExpander.TryComputeRootUseBounds(svg);
        Assert.NotNull(bounds);
        Assert.True(bounds!.Value.MinX < 0, "debe incluir x negativo");
        Assert.True(bounds.Value.MinY < 0, "debe incluir y negativo");
        Assert.True(bounds.Value.MaxX > 550 || bounds.Value.MaxY > 400
                    || bounds.Value.MaxX > 200, "cubre piezas root");

        var expanded = SvgFrameBoundsExpander.Expand(svg);
        Assert.True(expanded.Ok);
        Assert.Contains("viewBox=", expanded.SvgText, StringComparison.Ordinal);
        Assert.DoesNotContain("viewBox=\"0 0 550 400\"", expanded.SvgText, StringComparison.Ordinal);
        Assert.True(expanded.ContentBounds.MinX < 0);
        Assert.True(expanded.ContentBounds.MinY < 0);
        Assert.Equal(2, expanded.RootUseCount);
    }

    [Fact]
    public void SvgBounds_ignores_defs_uses()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                 width="100px" height="100px" viewBox="0 0 100 100">
              <defs>
                <g id="sprite1">
                  <use width="500" height="500" transform="matrix(1,0,0,1,-900,-900)" xlink:href="#shape1"/>
                </g>
                <g id="shape1"><path d="M0 0 L1 0 1 1 0 1 Z"/></g>
              </defs>
              <g>
                <use width="20" height="20" transform="matrix(1,0,0,1,10,10)" xlink:href="#shape1"/>
              </g>
            </svg>
            """;

        var bounds = SvgFrameBoundsExpander.TryComputeRootUseBounds(svg);
        Assert.NotNull(bounds);
        Assert.InRange(bounds!.Value.MinX, 9, 11);
        Assert.InRange(bounds.Value.MaxX, 29, 31);
        Assert.True(bounds.Value.MinX > -100, "defs use no debe inflar AABB");
    }

    [Fact]
    public void SvgSkia_full_root_rasterizer_then_normalize_to_256()
    {
        using var dir = new TempDir();
        var svgPath = Path.Combine(dir.Path, "1.svg");
        File.WriteAllText(svgPath, """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                 width="550px" height="400px" viewBox="0 0 550 400">
              <defs><g id="s"><path d="M0 0 L40 0 40 40 0 40 Z" fill="#336699"/></g></defs>
              <g>
                <use width="40" height="40" transform="matrix(1,0,0,1,-30,-50)" xlink:href="#s"/>
                <use width="40" height="40" transform="matrix(1,0,0,1,20,10)" xlink:href="#s"/>
              </g>
            </svg>
            """);

        var expandedPath = Path.Combine(dir.Path, "expanded.svg");
        var expand = SvgFrameBoundsExpander.ExpandFile(svgPath, expandedPath);
        Assert.True(expand.Ok);
        Assert.True(expand.ContentBounds.MinX < 0);
        Assert.True(expand.ContentBounds.MinY < 0);

        var raw = Path.Combine(dir.Path, "raw.png");
        var raster = SvgSkiaFrameRasterizer.RasterizeToPng(
            svgPath,
            raw,
            expand.ContentBounds,
            SvgSkiaFrameRasterizer.DefaultMargin,
            expand.Ok ? expandedPath : null);
        Assert.True(raster.Ok, raster.Error);
        Assert.True(File.Exists(raw));
        Assert.True(raster.OpaquePixelCount > 0);

        var staged = Path.Combine(dir.Path, "out.png");
        var crop = NpcGfxPreviewCropNormalizer.NormalizeFile(raw, staged);
        Assert.True(crop.HasContent);
        Assert.Equal(NpcGfxPreviewCropNormalizer.CanvasSize, crop.CanvasWidth);
        Assert.Equal(NpcGfxPreviewCropNormalizer.CanvasSize, crop.CanvasHeight);
        Assert.True(File.Exists(staged));
    }

    private static string? TryRealArtwork(int gfxId)
    {
        var desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "RUFUS RETRO", "resources", "app", "retroclient", "clips", "artworks", "big",
            gfxId + ".swf");
        if (File.Exists(desktop))
            return desktop;

        var env = Environment.GetEnvironmentVariable("RUFUS_CLIPS_ROOT");
        if (string.IsNullOrWhiteSpace(env))
            return null;
        var path = Path.Combine(env, "artworks", "big", gfxId + ".swf");
        return File.Exists(path) ? path : null;
    }

    private static NpcGfxPreviewPrepOptions BaseOpts(
        TempDir dir,
        string clips,
        string ffdec,
        string? library = null) =>
        new()
        {
            FfdecCliPath = ffdec,
            ClipsRoot = clips,
            StagingRoot = Path.Combine(dir.Path, "staging"),
            LibraryRoot = library,
            GfxIds = Array.Empty<int>(),
            ProcessTimeout = TimeSpan.FromSeconds(5),
        };

    private static string CreateClipsRoot(string root)
    {
        var clips = Path.Combine(root, "clips");
        Directory.CreateDirectory(Path.Combine(clips, "sprites"));
        Directory.CreateDirectory(Path.Combine(clips, "artworks", "big"));
        File.WriteAllText(Path.Combine(clips, "sprites", "sprites.xml"), "<sprites/>");
        return clips;
    }

    private static string CreateClipsWithArtwork(string root, int gfxId)
    {
        var clips = CreateClipsRoot(root);
        File.WriteAllBytes(
            Path.Combine(clips, "artworks", "big", gfxId + ".swf"),
            new byte[] { (byte)'F', (byte)'W', (byte)'S', 5 });
        return clips;
    }

    private static void WriteSparsePng(string path, int w, int h, int opaquePixels)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.Clear(Color.Transparent);
        var n = 0;
        for (var y = 0; y < h && n < opaquePixels; y++)
        {
            for (var x = 0; x < w && n < opaquePixels; x++)
            {
                bmp.SetPixel(x, y, Color.FromArgb(255, 20, 20, 20));
                n++;
            }
        }

        bmp.Save(path, ImageFormat.Png);
    }

    private static void WriteFilledRectPng(string path, int w, int h, Rectangle fill)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(Brushes.DarkOrange, fill);
        }

        bmp.Save(path, ImageFormat.Png);
    }

    private sealed class FakeFfdec : IFfdecProcessRunner
    {
        public bool TimedOut { get; set; }
        public int ExitCode { get; set; }
        public Action<string>? WritePng { get; set; }
        public Action<string>? WriteSvg { get; set; }
        public bool ShouldThrowIfCalled { get; set; }
        public bool WasCalled { get; private set; }

        public FfdecRunResult RunExportPng(
            string ffdecCliPath,
            string swfPath,
            string outputDirectory,
            SwfArtworkExportTargetSelector.ExportKind kind,
            int? spriteCharacterId,
            double zoom,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            WasCalled = true;
            if (ShouldThrowIfCalled)
                throw new InvalidOperationException("FFDec no debería llamarse");
            Directory.CreateDirectory(outputDirectory);
            if (!TimedOut && ExitCode == 0)
                WritePng?.Invoke(outputDirectory);
            return new FfdecRunResult
            {
                TimedOut = TimedOut,
                ExitCode = TimedOut ? -1 : ExitCode,
            };
        }

        public FfdecRunResult RunExportFrameSvg(
            string ffdecCliPath,
            string swfPath,
            string outputDirectory,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            WasCalled = true;
            Directory.CreateDirectory(outputDirectory);
            if (!TimedOut && ExitCode == 0)
                WriteSvg?.Invoke(outputDirectory);
            return new FfdecRunResult
            {
                TimedOut = TimedOut,
                ExitCode = TimedOut ? -1 : ExitCode,
            };
        }
    }

    private sealed class FakeInkscape : IInkscapeSvgRasterizer
    {
        public Action<string, string>? WritePng { get; set; }
        public bool TimedOut { get; set; }
        public int ExitCode { get; set; }

        public InkscapeRunResult RasterizePng(
            string inkscapeCliPath,
            string svgPath,
            string pngPath,
            bool exportAreaDrawing,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            if (!TimedOut && ExitCode == 0)
                WritePng?.Invoke(svgPath, pngPath);
            return new InkscapeRunResult
            {
                TimedOut = TimedOut,
                ExitCode = TimedOut ? -1 : ExitCode,
                PngProduced = !TimedOut && ExitCode == 0 && File.Exists(pngPath),
            };
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "rufus-npc-prep-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* ignore */ }
        }
    }
}
