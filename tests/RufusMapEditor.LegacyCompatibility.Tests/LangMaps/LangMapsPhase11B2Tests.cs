using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.LangMaps;

public sealed class LangMapsPhase11B2Tests
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

    private static string VersionsPipe(int maps) =>
        $"11&f=maps,es,{maps}|quests,es,1275|spells,es,1308|names,es,15";

    private static FakeLangSftpPublishClient SeedRemote(int mapsVersion, byte[] swfBytes)
    {
        var fake = new FakeLangSftpPublishClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", VersionsPipe(mapsVersion));
        fake.SeedFile($"/var/www/html/data/lang/swf/maps_es_{mapsVersion}.swf", swfBytes);
        return fake;
    }

    private static LangRemotePublishRequest MakeRequest(
        FakeLangSftpPublishClient fake,
        string work,
        string backup,
        int mapId = 30057,
        int x = 1,
        int y = 2,
        int sa = 10,
        int ep = 5) =>
        new()
        {
            Settings = new LangSftpSettings
            {
                Host = "test",
                User = "u",
                LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
                SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
            },
            PlainPassword = "x",
            MapId = mapId,
            X = x,
            Y = y,
            SubArea = sa,
            Ep = ep,
            WorkDirectory = work,
            BackupDirectory = backup,
            ClientFactory = (_, _) => fake,
        };

    [Fact]
    public void Bump_maps_token_preserves_others()
    {
        var src = VersionsPipe(1282);
        Assert.True(VersionsEsParser.TryBumpMapsVersion(src, 1282, 1283, out var bumped, out var err), err);
        Assert.Contains("maps,es,1283", bumped, StringComparison.Ordinal);
        Assert.Contains("quests,es,1275", bumped, StringComparison.Ordinal);
        Assert.Contains("spells,es,1308", bumped, StringComparison.Ordinal);
        Assert.DoesNotContain("maps,es,1282", bumped, StringComparison.Ordinal);
        Assert.True(VersionsEsParser.TryParseMapsVersion(bumped, out var v, out _));
        Assert.Equal(1283, v);
    }

    [Fact]
    public void Publish_N_to_N1_happy_path()
    {
        var swf = File.ReadAllBytes(FixtureSwf());
        var fake = SeedRemote(1282, swf);
        var work = Path.Combine(Path.GetTempPath(), "rufus-11b2-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "rufus-11b2-bak-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = LangRemotePublishService.Publish(MakeRequest(fake, work, backup));
            Assert.True(result.Success, result.Error);
            Assert.Equal(1282, result.SourceVersion);
            Assert.Equal(1283, result.TargetVersion);
            Assert.True(result.SwfUploaded);
            Assert.True(result.VersionsUpdated);
            Assert.Equal(1283, result.ActiveRemoteVersion);
            Assert.True(Directory.Exists(result.LocalBackupPath));
            Assert.True(File.Exists(Path.Combine(result.LocalBackupPath!, "maps_es_1282.swf")));
            Assert.True(File.Exists(Path.Combine(result.LocalBackupPath!, "versions_es.txt")));
            Assert.Equal(0, result.DeleteAttemptCount);
            Assert.Equal(0, fake.DeleteAttemptCount);
            Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/maps_es_1282.swf"));
            Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/maps_es_1283.swf"));
            Assert.Equal(result.LocalSwfSha256, result.RemoteSwfSha256);
            var remoteVersions = fake.PeekText("/var/www/html/data/lang/versions_es.txt");
            Assert.Contains("maps,es,1283", remoteVersions, StringComparison.Ordinal);
            Assert.Contains("quests,es,1275", remoteVersions, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* ignore */ }
            try { Directory.Delete(backup, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Publish_requires_backup_before_remote_write()
    {
        var swf = File.ReadAllBytes(FixtureSwf());
        var fake = SeedRemote(1282, swf);
        var work = Path.Combine(Path.GetTempPath(), "rufus-11b2-b-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "rufus-11b2-bb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = LangRemotePublishService.Publish(MakeRequest(fake, work, backup));
            Assert.True(result.Success, result.Error);
            Assert.False(string.IsNullOrWhiteSpace(result.LocalBackupPath));
            Assert.True(Directory.Exists(result.LocalBackupPath));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* ignore */ }
            try { Directory.Delete(backup, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Publish_blocks_when_remote_version_changed()
    {
        var swf = File.ReadAllBytes(FixtureSwf());
        var fake = SeedRemote(1282, swf);
        // After connect/sync read, mutate versions before concurrency check by wrapping factory
        var work = Path.Combine(Path.GetTempPath(), "rufus-11b2-c-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "rufus-11b2-cb-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Seed N+1 already in versions to simulate mid-flight change after first read:
            // Use a client that flips versions after first ReadAllText of versions.
            var flipping = new FlippingVersionsClient(fake, VersionsPipe(1282), VersionsPipe(9999));
            var result = LangRemotePublishService.Publish(new LangRemotePublishRequest
            {
                Settings = new LangSftpSettings { Host = "t", User = "u" },
                PlainPassword = "x",
                MapId = 30057,
                X = 1,
                Y = 2,
                SubArea = 10,
                Ep = 5,
                WorkDirectory = work,
                BackupDirectory = backup,
                ClientFactory = (_, _) => flipping,
            });
            Assert.False(result.Success);
            Assert.False(result.VersionsUpdated);
            Assert.Contains("cambiado", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* ignore */ }
            try { Directory.Delete(backup, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Publish_blocks_when_N1_already_exists()
    {
        var swf = File.ReadAllBytes(FixtureSwf());
        var fake = SeedRemote(1282, swf);
        fake.SeedFile("/var/www/html/data/lang/swf/maps_es_1283.swf", swf);
        var work = Path.Combine(Path.GetTempPath(), "rufus-11b2-e-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "rufus-11b2-eb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = LangRemotePublishService.Publish(MakeRequest(fake, work, backup));
            Assert.False(result.Success);
            Assert.False(result.SwfUploaded);
            Assert.False(result.VersionsUpdated);
            Assert.Contains("ya existe", result.Error!, StringComparison.OrdinalIgnoreCase);
            // N still active
            Assert.Contains("maps,es,1282", fake.PeekText("/var/www/html/data/lang/versions_es.txt"), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* ignore */ }
            try { Directory.Delete(backup, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Publish_hash_mismatch_blocks_versions_update()
    {
        var swf = File.ReadAllBytes(FixtureSwf());
        var fake = SeedRemote(1282, swf);
        var corrupting = new CorruptingUploadClient(fake);
        var work = Path.Combine(Path.GetTempPath(), "rufus-11b2-h-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "rufus-11b2-hb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = LangRemotePublishService.Publish(new LangRemotePublishRequest
            {
                Settings = new LangSftpSettings { Host = "t", User = "u" },
                PlainPassword = "x",
                MapId = 30057,
                X = 1,
                Y = 2,
                SubArea = 10,
                Ep = 5,
                WorkDirectory = work,
                BackupDirectory = backup,
                ClientFactory = (_, _) => corrupting,
            });
            Assert.False(result.Success);
            Assert.True(result.SwfUploaded);
            Assert.False(result.VersionsUpdated);
            Assert.Contains("Hash", result.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("maps,es,1282", fake.PeekText("/var/www/html/data/lang/versions_es.txt"), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* ignore */ }
            try { Directory.Delete(backup, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Publish_never_deletes_and_keeps_N()
    {
        var swf = File.ReadAllBytes(FixtureSwf());
        var fake = SeedRemote(1282, swf);
        var work = Path.Combine(Path.GetTempPath(), "rufus-11b2-d-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "rufus-11b2-db-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = LangRemotePublishService.Publish(MakeRequest(fake, work, backup));
            Assert.True(result.Success, result.Error);
            Assert.Equal(0, fake.DeleteAttemptCount);
            Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/maps_es_1282.swf"));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* ignore */ }
            try { Directory.Delete(backup, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Publish_logs_contain_no_password()
    {
        var swf = File.ReadAllBytes(FixtureSwf());
        var fake = SeedRemote(1282, swf);
        var work = Path.Combine(Path.GetTempPath(), "rufus-11b2-s-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "rufus-11b2-sb-" + Guid.NewGuid().ToString("N"));
        const string secret = "SuperSecretPassword99!";
        try
        {
            var result = LangRemotePublishService.Publish(new LangRemotePublishRequest
            {
                Settings = new LangSftpSettings { Host = "t", User = "u" },
                PlainPassword = secret,
                MapId = 30057,
                X = 1,
                Y = 2,
                SubArea = 10,
                Ep = 5,
                WorkDirectory = work,
                BackupDirectory = backup,
                ClientFactory = (_, _) => fake,
            });
            Assert.True(result.Success, result.Error);
            foreach (var line in result.LogLines)
                Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* ignore */ }
            try { Directory.Delete(backup, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Local_hash_stable()
    {
        var bytes = File.ReadAllBytes(FixtureSwf());
        var a = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var b = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    /// <summary>Returns N on first versions read, then flipped content afterwards.</summary>
    private sealed class FlippingVersionsClient : ILangSftpPublishClient
    {
        private readonly FakeLangSftpPublishClient _inner;
        private readonly string _first;
        private readonly string _next;
        private int _versionsReads;

        public FlippingVersionsClient(FakeLangSftpPublishClient inner, string first, string next)
        {
            _inner = inner;
            _first = first;
            _next = next;
        }

        public int WriteAttemptCount => _inner.WriteAttemptCount;
        public int DeleteAttemptCount => 0;

        public void Connect() => _inner.Connect();
        public bool FileExists(string remotePath) => _inner.FileExists(remotePath);
        public bool DirectoryExists(string remotePath) => _inner.DirectoryExists(remotePath);
        public byte[] DownloadBytes(string remotePath) => _inner.DownloadBytes(remotePath);
        public long GetFileLength(string remotePath) => _inner.GetFileLength(remotePath);
        public void UploadNewFile(string remotePath, byte[] content) => _inner.UploadNewFile(remotePath, content);
        public void ReplaceFileAtomically(string remotePath, byte[] content, string backupRemotePath) =>
            _inner.ReplaceFileAtomically(remotePath, content, backupRemotePath);
        public void Dispose() => _inner.Dispose();

        public string ReadAllText(string remotePath)
        {
            if (remotePath.EndsWith("versions_es.txt", StringComparison.Ordinal))
            {
                _versionsReads++;
                return _versionsReads == 1 ? _first : _next;
            }

            return _inner.ReadAllText(remotePath);
        }
    }

    /// <summary>Corrupts bytes after upload so re-download hash mismatches.</summary>
    private sealed class CorruptingUploadClient : ILangSftpPublishClient
    {
        private readonly FakeLangSftpPublishClient _inner;

        public CorruptingUploadClient(FakeLangSftpPublishClient inner) => _inner = inner;

        public int WriteAttemptCount => _inner.WriteAttemptCount;
        public int DeleteAttemptCount => 0;

        public void Connect() => _inner.Connect();
        public bool FileExists(string remotePath) => _inner.FileExists(remotePath);
        public bool DirectoryExists(string remotePath) => _inner.DirectoryExists(remotePath);
        public string ReadAllText(string remotePath) => _inner.ReadAllText(remotePath);
        public byte[] DownloadBytes(string remotePath) => _inner.DownloadBytes(remotePath);
        public long GetFileLength(string remotePath) => _inner.GetFileLength(remotePath);
        public void ReplaceFileAtomically(string remotePath, byte[] content, string backupRemotePath) =>
            _inner.ReplaceFileAtomically(remotePath, content, backupRemotePath);
        public void Dispose() => _inner.Dispose();

        public void UploadNewFile(string remotePath, byte[] content)
        {
            var corrupted = (byte[])content.Clone();
            corrupted[^1] ^= 0xFF;
            _inner.UploadNewFile(remotePath, corrupted);
        }
    }
}
