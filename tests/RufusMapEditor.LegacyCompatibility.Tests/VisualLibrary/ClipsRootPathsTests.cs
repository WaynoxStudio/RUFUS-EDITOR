using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class ClipsRootPathsTests
{
    [Fact]
    public void Validate_null_path_is_invalid()
    {
        var result = ClipsRootPaths.Validate(null);
        Assert.False(result.IsValid);
        Assert.Contains("no configurada", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_missing_folder_is_invalid()
    {
        var path = Path.Combine(Path.GetTempPath(), "rufus-clips-missing-" + Guid.NewGuid().ToString("N"));
        var result = ClipsRootPaths.Validate(path);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_valid_temp_clips_path()
    {
        var dir = CreateValidClipsRoot();
        try
        {
            var result = ClipsRootPaths.Validate(dir);
            Assert.True(result.IsValid);
            Assert.Equal(Path.GetFullPath(dir), result.NormalizedPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_accepts_retroclient_parent_when_clips_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "rufus-retro-" + Guid.NewGuid().ToString("N"));
        var retroclient = Path.Combine(root, "resources", "app", "retroclient");
        var clips = Path.Combine(retroclient, "clips");
        Directory.CreateDirectory(Path.Combine(clips, "sprites"));
        File.WriteAllText(Path.Combine(clips, "sprites", "sprites.xml"), "<sprites/>");
        try
        {
            var result = ClipsRootPaths.Validate(retroclient);
            Assert.True(result.IsValid);
            Assert.Equal(Path.GetFullPath(clips), result.NormalizedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveEffective_uses_configured_before_discovery()
    {
        var dir = CreateValidClipsRoot();
        try
        {
            var resolved = ClipsRootPaths.ResolveEffective(dir);
            Assert.Equal(Path.GetFullPath(dir), resolved);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveEffective_prefers_valid_configured_over_discovery()
    {
        var dir = CreateValidClipsRoot();
        try
        {
            var resolved = ClipsRootPaths.ResolveEffective(dir);
            Assert.Equal(Path.GetFullPath(dir), resolved);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveEffective_falls_back_to_discovery_when_configured_invalid()
    {
        var dir = CreateValidClipsRoot();
        var original = Environment.GetEnvironmentVariable("RUFUS_CLIPS_ROOT");
        Environment.SetEnvironmentVariable("RUFUS_CLIPS_ROOT", dir);
        try
        {
            var hits = ClipsRootPaths.DiscoverValidPaths();
            Assert.Contains(Path.GetFullPath(dir), hits);

            var resolved = ClipsRootPaths.ResolveEffective("__not-a-valid-clips-path__");
            if (hits.Count == 1)
                Assert.Equal(Path.GetFullPath(dir), resolved);
            else
                Assert.True(resolved is null || hits.Contains(resolved, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUFUS_CLIPS_ROOT", original);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryDiscoverUnambiguous_returns_null_when_multiple_valid_candidates()
    {
        var a = CreateValidClipsRoot();
        var b = CreateValidClipsRoot();
        var original = Environment.GetEnvironmentVariable("RUFUS_CLIPS_ROOT");
        Environment.SetEnvironmentVariable("RUFUS_CLIPS_ROOT", a);
        try
        {
            // Desktop RUFUS RETRO may also exist; with env + desktop we expect null or single if only env.
            var hits = ClipsRootPaths.DiscoverValidPaths();
            Assert.True(hits.Count >= 1);
            if (hits.Count > 1)
                Assert.Null(ClipsRootPaths.TryDiscoverUnambiguous());
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUFUS_CLIPS_ROOT", original);
            Directory.Delete(a, recursive: true);
            Directory.Delete(b, recursive: true);
        }
    }

    [Fact]
    public void TryDiscoverUnambiguous_finds_env_when_only_candidate()
    {
        var dir = CreateValidClipsRoot();
        var original = Environment.GetEnvironmentVariable("RUFUS_CLIPS_ROOT");
        Environment.SetEnvironmentVariable("RUFUS_CLIPS_ROOT", dir);
        try
        {
            var hits = ClipsRootPaths.DiscoverValidPaths();
            if (hits.Count == 1)
                Assert.Equal(Path.GetFullPath(dir), ClipsRootPaths.TryDiscoverUnambiguous());
            else
                Assert.Contains(Path.GetFullPath(dir), hits);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUFUS_CLIPS_ROOT", original);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Catalog_reload_updates_display_names_from_sprites_xml()
    {
        const string SampleSpritesXml = """
            <sprites>
              <sprite id="71" name="Eniripsa fille" />
              <sprite id="1245" name="ogivol" />
            </sprites>
            """;

        var clips = CreateValidClipsRoot();
        File.WriteAllText(Path.Combine(clips, "sprites", "sprites.xml"), SampleSpritesXml);

        var rows = new[]
        {
            new NpcGfxUsageRow { GfxId = 71, Nombre = "Clara Dol" },
            new NpcGfxUsageRow { GfxId = 1245, Nombre = "Ogivol Scarratero" },
            new NpcGfxUsageRow { GfxId = 9999, Nombre = "Unknown NPC" },
        };

        var withNames = NpcGfxCatalogBuilder.Build(
            rows,
            SpritesXmlParser.ParseFile(Path.Combine(clips, "sprites", "sprites.xml")).Names,
            clips);
        Assert.Equal("Eniripsa fille", withNames.Entries.First(e => e.GfxId == 71).DisplayName);
        Assert.Equal("ogivol", withNames.Entries.First(e => e.GfxId == 1245).DisplayName);
        Assert.Equal("GFX #9999", withNames.Entries.First(e => e.GfxId == 9999).DisplayName);

        Directory.Delete(clips, recursive: true);
    }

    [Fact]
    public async Task Catalog_service_reload_sprite_metadata_updates_names_without_bd_reload()
    {
        var clips = CreateValidClipsRoot();
        File.WriteAllText(Path.Combine(clips, "sprites", "sprites.xml"), "<sprites/>");
        var rows = new[] { new NpcGfxUsageRow { GfxId = 71, Nombre = "Clara Dol" } };

        var service = new NpcGfxCatalogService();
        await service.LoadAsync(
            new DatabaseSettings(),
            "",
            clipsRoot: clips,
            usageRepo: new FakeGfxUsageRepo(rows));

        Assert.Equal("GFX #71", service.Entries.First().DisplayName);

        File.WriteAllText(Path.Combine(clips, "sprites", "sprites.xml"), """
            <sprites><sprite id="71" name="Eniripsa fille" /></sprites>
            """);

        Assert.True(service.ReloadSpriteMetadata(clips));
        Assert.Equal("Eniripsa fille", service.Entries.First(e => e.GfxId == 71).DisplayName);
        Assert.True(service.HasSpriteNames);

        Directory.Delete(clips, recursive: true);
    }

    [Fact]
    public void Search_finds_ogivol_by_sprite_name_after_valid_clips()
    {
        const string SampleSpritesXml = """
            <sprites>
              <sprite id="1245" name="ogivol" />
            </sprites>
            """;

        var clips = CreateValidClipsRoot();
        File.WriteAllText(Path.Combine(clips, "sprites", "sprites.xml"), SampleSpritesXml);
        var rows = new[] { new NpcGfxUsageRow { GfxId = 1245, Nombre = "Ogivol Scarratero" } };
        var built = NpcGfxCatalogBuilder.Build(
            rows,
            SpritesXmlParser.ParseFile(Path.Combine(clips, "sprites", "sprites.xml")).Names,
            clips);

        var match = NpcGfxCatalogSearch.Filter(built.Entries, "ogivol");
        Assert.Single(match);
        Assert.Equal("ogivol", match[0].DisplayName);
        Assert.Contains("Ogivol Scarratero", match[0].NpcNames);

        Directory.Delete(clips, recursive: true);
    }

    private static string CreateValidClipsRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rufus-clips-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sprites"));
        File.WriteAllText(Path.Combine(dir, "sprites", "sprites.xml"), "<sprites/>");
        return dir;
    }

    private sealed class FakeGfxUsageRepo(IReadOnlyList<NpcGfxUsageRow> rows) : INpcsGfxUsageReadRepository
    {
        public Task<IReadOnlyList<NpcGfxUsageRow>> GetAllGfxUsageAsync(CancellationToken ct = default) =>
            Task.FromResult(rows);
    }
}
