using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.LangMaps;

public sealed class LangMapsPhase11B1Tests
{
    private static string FixtureSwf()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps_es_1282.swf");
        if (!File.Exists(path))
            path = Path.Combine(FindRepoRoot(), "tests", "RufusMapEditor.LegacyCompatibility.Tests", "Fixtures", "maps_es_1282.swf");
        Assert.True(File.Exists(path), "Fixture maps_es_1282.swf missing");
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

    private static string RealisticVersionsEs =>
        """
        # dofus lang versions
        quests,es,401
        spells,es,220
        itemsets,es,88
        names,es,15
        servers,es,3
        maps,es,1282
        speech,es,12
        """;

    [Fact]
    public void Parses_maps_es_token_from_realistic_versions()
    {
        Assert.True(VersionsEsParser.TryParseMapsVersion(RealisticVersionsEs, out var v, out var err), err);
        Assert.Equal(1282, v);
        Assert.Equal("maps,es,1282", VersionsEsParser.ExtractMapsLine(RealisticVersionsEs));
    }

    [Fact]
    public void Does_not_confuse_quests_spells_with_maps()
    {
        var text = "quests,es,1282\nspells,es,1282\nmaps,es,99\n";
        Assert.True(VersionsEsParser.TryParseMapsVersion(text, out var v, out _));
        Assert.Equal(99, v);
    }

    [Fact]
    public void Builds_swf_file_name()
    {
        Assert.Equal("maps_es_1282.swf", VersionsEsParser.BuildSwfFileName(1282));
    }

    [Fact]
    public void Missing_maps_token_fails()
    {
        Assert.False(VersionsEsParser.TryParseMapsVersion("quests,es,1\nspells,es,2\n", out _, out var err));
        Assert.Contains("maps,es", err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sync_readonly_downloads_and_validates_with_11a()
    {
        var swfBytes = File.ReadAllBytes(FixtureSwf());
        var fake = new FakeLangSftpReadClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", RealisticVersionsEs);
        fake.SeedFile("/var/www/html/data/lang/swf/maps_es_1282.swf", swfBytes);

        var cache = Path.Combine(Path.GetTempPath(), "rufus-11b1-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = LangRemoteSyncService.Sync(new LangRemoteSyncRequest
            {
                Settings = new LangSftpSettings
                {
                    Host = "test",
                    User = "u",
                    LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
                    SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
                },
                PlainPassword = "x",
                CacheDirectory = cache,
                ClientFactory = (_, _) => fake,
            });

            Assert.True(result.Success, result.Error);
            Assert.Equal(1282, result.MapsVersion);
            Assert.Equal("maps_es_1282.swf", result.SwfFileName);
            Assert.Equal(1282, result.InternalVersion);
            Assert.True(result.VersionsMatch);
            Assert.True(result.MaEntryCount > 1000);
            Assert.Equal(0, result.RemoteWriteAttempts);
            Assert.Equal(0, fake.WriteAttemptCount);
            Assert.True(File.Exists(result.LocalCachePath));
            Assert.False(string.IsNullOrWhiteSpace(result.SwfSha256));
            Assert.Equal("SINCRONIZADO", result.StatusLabel);
            Assert.NotNull(result.Snapshot);
            Assert.Equal(1282, result.Snapshot!.MapsVersion);
        }
        finally
        {
            try { Directory.Delete(cache, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Sync_blocks_on_internal_version_mismatch()
    {
        var swfBytes = File.ReadAllBytes(FixtureSwf());
        var fake = new FakeLangSftpReadClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", "maps,es,9999\n");
        fake.SeedFile("/var/www/html/data/lang/swf/maps_es_9999.swf", swfBytes);

        var cache = Path.Combine(Path.GetTempPath(), "rufus-11b1-mm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = LangRemoteSyncService.Sync(new LangRemoteSyncRequest
            {
                Settings = new LangSftpSettings { Host = "t", User = "u" },
                PlainPassword = "x",
                CacheDirectory = cache,
                ClientFactory = (_, _) => fake,
            });

            Assert.False(result.Success);
            Assert.Equal(9999, result.MapsVersion);
            Assert.Equal(1282, result.InternalVersion);
            Assert.False(result.VersionsMatch);
            Assert.Contains("distinta", result.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, fake.WriteAttemptCount);
        }
        finally
        {
            try { Directory.Delete(cache, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Sync_errors_when_swf_missing()
    {
        var fake = new FakeLangSftpReadClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", "maps,es,1282\n");

        var result = LangRemoteSyncService.Sync(new LangRemoteSyncRequest
        {
            Settings = new LangSftpSettings { Host = "t", User = "u" },
            PlainPassword = "x",
            CacheDirectory = Path.GetTempPath(),
            ClientFactory = (_, _) => fake,
        });

        Assert.False(result.Success);
        Assert.Contains("inexistente", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fake.WriteAttemptCount);
    }

    [Fact]
    public void Sync_never_invokes_remote_writes()
    {
        var swfBytes = File.ReadAllBytes(FixtureSwf());
        var fake = new FakeLangSftpReadClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", "maps,es,1282\n");
        fake.SeedFile("/var/www/html/data/lang/swf/maps_es_1282.swf", swfBytes);

        var cache = Path.Combine(Path.GetTempPath(), "rufus-11b1-w-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = LangRemoteSyncService.Sync(new LangRemoteSyncRequest
            {
                Settings = new LangSftpSettings { Host = "t", User = "u" },
                PlainPassword = "x",
                CacheDirectory = cache,
                ClientFactory = (_, _) => fake,
            });
            Assert.Equal(0, fake.WriteAttemptCount);
        }
        finally
        {
            try { Directory.Delete(cache, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Read_client_surface_has_no_write_apis()
    {
        var names = typeof(ILangSftpReadClient).GetMethods()
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Upload", names);
        Assert.DoesNotContain("Delete", names);
        Assert.DoesNotContain("Rename", names);
        Assert.DoesNotContain("WriteRemoteText", names);
        Assert.DoesNotContain("Move", names);
        Assert.DoesNotContain("UploadFile", names);
        Assert.DoesNotContain("WriteAllText", names);
    }
}
