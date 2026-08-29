using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class ContConn1Tests
{
    [Fact]
    public void FormatStateLabel_defaults_and_connected()
    {
        Assert.Equal("● Sin comprobar", ContentSharedConnectionProbe.FormatStateLabel(SharedConnectionState.Unchecked, true));
        Assert.Equal("● Conectada", ContentSharedConnectionProbe.FormatStateLabel(SharedConnectionState.Connected, true));
        Assert.Equal("● Conectado", ContentSharedConnectionProbe.FormatStateLabel(SharedConnectionState.Connected, false));
        Assert.Equal("● Error", ContentSharedConnectionProbe.FormatStateLabel(SharedConnectionState.Error, false));
    }

    [Fact]
    public void SanitizeError_hides_password_mentions()
    {
        var msg = ContentSharedConnectionProbe.SanitizeError(
            new InvalidOperationException("Access denied for user with bad password"));
        Assert.DoesNotContain("password", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("denegado", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeDatabase_uses_shared_mapas_repository_test()
    {
        var called = false;
        await ContentSharedConnectionProbe.ProbeDatabaseAsync(
            new DatabaseSettings { Host = "h", User = "u", Port = 3306, Database = "estaticos" },
            "secret",
            (_, _) =>
            {
                called = true;
                return new InMemoryMapasRepository();
            });
        Assert.True(called);
    }

    [Fact]
    public void ProbeSftp_checks_lang_and_swf_directories_readonly()
    {
        var fake = new FakeLangSftpReadClient();
        fake.SeedDirectory(LangSftpSettings.DefaultLangRemotePath);
        fake.SeedDirectory(LangSftpSettings.DefaultSwfRemotePath);

        var settings = new LangSftpSettings
        {
            Host = "sftp.example",
            Port = 22,
            User = "ubuntu",
            LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
            SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
        };

        var msg = ContentSharedConnectionProbe.ProbeSftp(settings, "x", (_, _) => fake);
        Assert.Contains("lang", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fake.WriteAttemptCount);
    }

    [Fact]
    public void ProbeSftp_fails_when_swf_directory_missing()
    {
        var fake = new FakeLangSftpReadClient();
        fake.SeedDirectory(LangSftpSettings.DefaultLangRemotePath);

        var settings = new LangSftpSettings
        {
            Host = "sftp.example",
            User = "ubuntu",
            LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
            SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentSharedConnectionProbe.ProbeSftp(settings, "x", (_, _) => fake));
        Assert.Contains("swf", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fake.WriteAttemptCount);
    }

    [Fact]
    public void ProbeSftp_accepts_directory_implied_by_seeded_files()
    {
        var fake = new FakeLangSftpReadClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", "maps,es,1\n");
        fake.SeedFile("/var/www/html/data/lang/swf/maps_es_1.swf", new byte[] { 1, 2, 3 });

        var settings = new LangSftpSettings
        {
            Host = "h",
            User = "u",
            LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
            SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
        };

        var msg = ContentSharedConnectionProbe.ProbeSftp(settings, "x", (_, _) => fake);
        Assert.Contains("OK", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fake.WriteAttemptCount);
    }

    [Fact]
    public void Probe_reuses_mapas_settings_types_only()
    {
        var db = typeof(ContentSharedConnectionProbe).GetMethod(nameof(ContentSharedConnectionProbe.ProbeDatabaseAsync));
        Assert.NotNull(db);
        Assert.Equal(typeof(DatabaseSettings), db!.GetParameters()[0].ParameterType);

        var sftp = typeof(ContentSharedConnectionProbe).GetMethod(nameof(ContentSharedConnectionProbe.ProbeSftp));
        Assert.NotNull(sftp);
        Assert.Equal(typeof(LangSftpSettings), sftp!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void Content_module_loads_global_appsettings_without_duplicate_credential_types()
    {
        var root = FindRepoRoot();
        var vm = File.ReadAllText(Path.Combine(root, "src", "RufusMapEditor.App", "ViewModels", "ContentWorkspaceViewModel.cs"));
        Assert.Contains("AppSettingsStore.Load()", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDatabase", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentSftp", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentLangSftp", vm, StringComparison.Ordinal);

        var settings = File.ReadAllText(Path.Combine(root, "src", "RufusMapEditor.App", "Services", "AppSettingsStore.cs"));
        Assert.Contains("public DatabaseSettings Database", settings, StringComparison.Ordinal);
        Assert.Contains("public LangSftpSettings LangSftp", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDatabase", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentSftp", settings, StringComparison.Ordinal);
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
}
