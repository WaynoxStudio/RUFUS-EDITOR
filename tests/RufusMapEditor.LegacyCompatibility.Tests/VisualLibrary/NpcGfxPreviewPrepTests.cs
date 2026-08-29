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
        public bool ShouldThrowIfCalled { get; set; }
        public bool WasCalled { get; private set; }

        public FfdecRunResult RunExportFramePng(
            string ffdecCliPath,
            string swfPath,
            string outputDirectory,
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
