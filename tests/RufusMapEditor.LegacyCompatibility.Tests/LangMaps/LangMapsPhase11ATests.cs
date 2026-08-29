using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.LangMaps;

public sealed class LangMapsPhase11ATests
{
    private static string FixturePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps_es_1282.swf");
        if (!File.Exists(path))
            path = Path.Combine(FindRepoRoot(), "tests", "RufusMapEditor.LegacyCompatibility.Tests", "Fixtures", "maps_es_1282.swf");
        Assert.True(File.Exists(path), $"Fixture missing: {path}");
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "RufusMapEditor.LegacyCompatibility")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string TempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "rufus-11a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Reads_real_cws_and_version()
    {
        var info = LangMapsSwfService.Inspect(FixturePath());
        Assert.True(info.WasCompressed);
        Assert.Equal(1282, info.Version);
        Assert.True(info.EntryCount > 1000);
    }

    [Fact]
    public void Locates_entries_via_public_inspect()
    {
        var info = LangMapsSwfService.Inspect(FixturePath());
        Assert.Contains(info.Entries, e => e.MapId == 31324);
        var e = info.Entries.Single(x => x.MapId == 31324);
        Assert.Equal(0, e.X);
        Assert.Equal(-4, e.Y);
        Assert.Equal(684, e.SubArea);
        Assert.Equal(2, e.Ep);
    }

    [Fact]
    public void Insert_new_ma_m_increments_version_and_validates()
    {
        var src = FixturePath();
        var original = File.ReadAllBytes(src);
        var outDir = TempDir();
        try
        {
            var result = LangMapsSwfService.Generate(new LangMapsGenerateRequest
            {
                SourceSwfPath = src,
                OutputDirectory = outDir,
                MapId = 30057,
                X = 12,
                Y = -7,
                SubArea = 42,
                Ep = 2,
            });
            Assert.True(result.Success, result.Error);
            Assert.True(result.Inserted);
            Assert.False(result.Updated);
            Assert.Equal(1282, result.SourceVersion);
            Assert.Equal(1283, result.TargetVersion);
            Assert.Equal(Path.Combine(outDir, "maps_es_1283.swf"), result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));

            var re = LangMapsSwfService.Inspect(result.OutputPath!);
            Assert.Equal(1283, re.Version);
            var e = Assert.Single(re.Entries, x => x.MapId == 30057);
            Assert.Equal(12, e.X);
            Assert.Equal(-7, e.Y);
            Assert.Equal(42, e.SubArea);
            Assert.Equal(2, e.Ep);
            Assert.True(File.ReadAllBytes(src).SequenceEqual(original));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Update_existing_ma_m_does_not_duplicate()
    {
        var src = FixturePath();
        var outDir = TempDir();
        try
        {
            var result = LangMapsSwfService.Generate(new LangMapsGenerateRequest
            {
                SourceSwfPath = src,
                OutputDirectory = outDir,
                MapId = 31324,
                X = 1,
                Y = 2,
                SubArea = 3,
                Ep = 4,
            });
            Assert.True(result.Success, result.Error);
            Assert.True(result.Updated);
            Assert.False(result.Inserted);

            var re = LangMapsSwfService.Inspect(result.OutputPath!);
            Assert.Equal(1283, re.Version);
            Assert.Single(re.Entries, x => x.MapId == 31324);
            var e = re.Entries.Single(x => x.MapId == 31324);
            Assert.Equal(1, e.X);
            Assert.Equal(2, e.Y);
            Assert.Equal(3, e.SubArea);
            Assert.Equal(4, e.Ep);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Missing_ep_blocks_generation()
    {
        var result = LangMapsSwfService.Generate(new LangMapsGenerateRequest
        {
            SourceSwfPath = FixturePath(),
            OutputDirectory = TempDir(),
            MapId = 30057,
            X = 0,
            Y = 0,
            SubArea = 1,
            Ep = null,
        });
        Assert.False(result.Success);
        Assert.Equal(LangMapsSwfService.EpUndefinedMessage, result.Error);
    }

    [Fact]
    public void Rebuilds_cws_and_reopens()
    {
        var outDir = TempDir();
        try
        {
            var result = LangMapsSwfService.Generate(new LangMapsGenerateRequest
            {
                SourceSwfPath = FixturePath(),
                OutputDirectory = outDir,
                MapId = 999001,
                X = -47,
                Y = 33,
                SubArea = 10,
                Ep = 1,
            });
            Assert.True(result.Success, result.Error);
            var bytes = File.ReadAllBytes(result.OutputPath!);
            Assert.Equal((byte)'C', bytes[0]);
            Assert.Equal((byte)'W', bytes[1]);
            Assert.Equal((byte)'S', bytes[2]);
            var info = LangMapsSwfService.Inspect(result.OutputPath!);
            Assert.True(info.WasCompressed);
            Assert.Equal(1283, info.Version);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }
}
