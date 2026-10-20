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

    [Fact]
    public void Dual_variants_same_coords_are_kept()
    {
        var outDir = TempDir();
        try
        {
            var result = LangMapsSwfService.GenerateBatch(new LangMapsBatchGenerateRequest
            {
                SourceSwfPath = FixturePath(),
                OutputDirectory = outDir,
                Entries =
                [
                    new LangMapsBatchEntry { MapId = 30017, X = -44, Y = -38, SubArea = 30, Ep = 2 },
                    new LangMapsBatchEntry { MapId = 30017, X = -44, Y = -38, SubArea = 612, Ep = 18 },
                ],
            });
            Assert.True(result.Success, result.Error);
            Assert.Equal(1283, result.TargetVersion);

            var re = LangMapsSwfService.Inspect(result.OutputPath!);
            var hits = re.Entries.Where(e => e.MapId == 30017).OrderBy(e => e.Ep).ToList();
            Assert.Equal(2, hits.Count);
            Assert.All(hits, e =>
            {
                Assert.Equal(-44, e.X);
                Assert.Equal(-38, e.Y);
            });
            Assert.Contains(hits, e => e.SubArea == 30 && e.Ep == 2);
            Assert.Contains(hits, e => e.SubArea == 612 && e.Ep == 18);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Dual_variants_divergent_coords_are_rejected()
    {
        var outDir = TempDir();
        try
        {
            var result = LangMapsSwfService.GenerateBatch(new LangMapsBatchGenerateRequest
            {
                SourceSwfPath = FixturePath(),
                OutputDirectory = outDir,
                Entries =
                [
                    new LangMapsBatchEntry { MapId = 30017, X = -44, Y = -38, SubArea = 30, Ep = 2 },
                    new LangMapsBatchEntry { MapId = 30017, X = -47, Y = -35, SubArea = 612, Ep = 18 },
                ],
            });
            Assert.False(result.Success);
            Assert.Contains("coords divergentes", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Batch_can_remove_mapids_in_same_bump()
    {
        var outDir = TempDir();
        try
        {
            // Insert a throwaway id, then regenerate from that SWF removing it while updating another.
            var step1 = LangMapsSwfService.Generate(new LangMapsGenerateRequest
            {
                SourceSwfPath = FixturePath(),
                OutputDirectory = outDir,
                MapId = 30012,
                X = -43,
                Y = -34,
                SubArea = 30,
                Ep = 2,
            });
            Assert.True(step1.Success, step1.Error);

            var step2Dir = TempDir();
            try
            {
                var step2 = LangMapsSwfService.GenerateBatch(new LangMapsBatchGenerateRequest
                {
                    SourceSwfPath = step1.OutputPath!,
                    OutputDirectory = step2Dir,
                    Entries =
                    [
                        new LangMapsBatchEntry { MapId = 31324, X = 0, Y = -4, SubArea = 684, Ep = 2 },
                    ],
                    RemoveMapIds = [30012],
                });
                Assert.True(step2.Success, step2.Error);
                var re = LangMapsSwfService.Inspect(step2.OutputPath!);
                Assert.DoesNotContain(re.Entries, e => e.MapId == 30012);
                Assert.Contains(re.Entries, e => e.MapId == 31324);
            }
            finally
            {
                try { Directory.Delete(step2Dir, true); } catch { /* ignore */ }
            }
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }
}
