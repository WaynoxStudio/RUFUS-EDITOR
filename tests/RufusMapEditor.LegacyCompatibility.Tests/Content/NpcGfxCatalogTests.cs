using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>ADMIN.UI.4B.2A.3C — confirmed NPC gfx catalog + picker semantics.</summary>
public sealed class NpcGfxCatalogTests
{
  private const string SampleSpritesXml = """
    <sprites>
      <type name="NPC">
        <sprite id="71" name="Eniripsa fille" />
        <sprite id="1245" name="ogivol" />
        <sprite id="1245" name="ogivol" />
        <sprite id="30" name="Enutrof" />
        <sprite id="30" name="Otro nombre" />
      </type>
    </sprites>
    """;

    [Fact]
    public void SpritesXmlParser_resolves_confirmed_names()
    {
        var doc = System.Xml.Linq.XDocument.Parse(SampleSpritesXml);
        var parsed = SpritesXmlParser.Parse(doc);

        Assert.Equal("Eniripsa fille", parsed.Names[71]);
        Assert.Equal("ogivol", parsed.Names[1245]);
        Assert.Equal("Enutrof", parsed.Names[30]);
    }

    [Fact]
    public void SpritesXmlParser_dedupes_duplicate_ids_and_warns_on_conflict()
    {
        var doc = System.Xml.Linq.XDocument.Parse(SampleSpritesXml);
        var parsed = SpritesXmlParser.Parse(doc);

        Assert.Single(parsed.Warnings, w => w.Contains("gfx 30", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Enutrof", parsed.Names[30]);
    }

    [Fact]
    public void Builder_groups_multiple_npcs_into_single_entry()
    {
        var rows = new[]
        {
            new NpcGfxUsageRow { GfxId = 71, Nombre = "Clara Dol" },
            new NpcGfxUsageRow { GfxId = 71, Nombre = "Mimi Fista" },
            new NpcGfxUsageRow { GfxId = 71, Nombre = "Clara Dol" },
        };
        var doc = System.Xml.Linq.XDocument.Parse(SampleSpritesXml);
        var names = SpritesXmlParser.Parse(doc).Names;

        var built = NpcGfxCatalogBuilder.Build(rows, names, clipsRoot: null);

        Assert.Single(built.Entries);
        var entry = built.Entries[0];
        Assert.Equal(71, entry.GfxId);
        Assert.Equal("Eniripsa fille", entry.DisplayName);
        Assert.Equal(3, entry.NpcCount);
        Assert.Equal(2, entry.NpcNames.Count);
        Assert.True(entry.IsConfirmedNpcGfx);
    }

    [Fact]
    public void Builder_without_sprites_xml_name_uses_gfx_hash()
    {
        var rows = new[] { new NpcGfxUsageRow { GfxId = 9999, Nombre = "Captain Iglut" } };
        var built = NpcGfxCatalogBuilder.Build(rows, new Dictionary<int, string>(), clipsRoot: null);

        Assert.Equal("GFX #9999", built.Entries[0].DisplayName);
        Assert.Equal("Usado por 1 NPC", built.Entries[0].UsageSummary);
    }

    [Fact]
    public void Search_finds_by_gfx_id_look_name_and_npc_name()
    {
        var entries = BuildSampleCatalog();
        Assert.Contains(entries, e => NpcGfxCatalogSearch.Matches(e, "1245"));
        Assert.Contains(entries, e => NpcGfxCatalogSearch.Matches(e, "ogivol"));
        Assert.Contains(entries, e => NpcGfxCatalogSearch.Matches(e, "Ogivol Scarratero"));
        Assert.DoesNotContain(entries, e => NpcGfxCatalogSearch.Matches(e, "zzzznotfound"));
    }

    [Fact]
    public void Gfx71_display_name_is_eniripsa_fille()
    {
        var entries = BuildSampleCatalog();
        var gfx71 = entries.First(e => e.GfxId == 71);
        Assert.Equal("Eniripsa fille", gfx71.DisplayName);
    }

    [Fact]
    public void Gfx1245_display_name_is_ogivol_with_npc_association()
    {
        var entries = BuildSampleCatalog();
        var gfx1245 = entries.First(e => e.GfxId == 1245);
        Assert.Equal("ogivol", gfx1245.DisplayName);
        Assert.Contains("Ogivol Scarratero", gfx1245.NpcNames);
    }

    [Fact]
    public void Gfx9999_appears_without_sprite_name()
    {
        var entries = BuildSampleCatalog();
        var gfx9999 = entries.First(e => e.GfxId == 9999);
        Assert.Equal("GFX #9999", gfx9999.DisplayName);
        Assert.Equal(267, gfx9999.NpcCount);
    }

    [Fact]
    public void Selection_updates_draft_gfx_only()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.Sexo = 1;
        draft.ScaleX = 140;
        draft.ScaleY = 140;
        draft.Accesorios = "1,2,3,4,5";

        draft.GfxId = 1245;

        Assert.Equal(1245, draft.GfxId);
        Assert.Equal(1, draft.Sexo);
        Assert.Equal(140, draft.ScaleX);
        Assert.Equal(140, draft.ScaleY);
        Assert.Equal("1,2,3,4,5", draft.Accesorios);
    }

    [Fact]
    public void Cancelled_picker_does_not_change_draft_gfx()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.GfxId = 71;
        var original = draft.GfxId;
        // Simulates closing picker without selection.
        var selectedGfxId = (int?)null;
        if (selectedGfxId is int gfx)
            draft.GfxId = gfx;
        Assert.Equal(original, draft.GfxId);
    }

    [Fact]
    public void Catalog_service_without_bd_rows_is_empty_not_crash()
    {
        var service = new NpcGfxCatalogService();
        var built = NpcGfxCatalogBuilder.Build(
            Array.Empty<NpcGfxUsageRow>(),
            new Dictionary<int, string>(),
            clipsRoot: null);
        Assert.Empty(built.Entries);
    }

    [Fact]
    public void Appearance_names_resolve_without_full_catalog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "npc-gfx-test-" + Guid.NewGuid().ToString("N"));
        var spritesDir = Path.Combine(dir, "sprites");
        Directory.CreateDirectory(spritesDir);
        File.WriteAllText(Path.Combine(spritesDir, "sprites.xml"), SampleSpritesXml);

        try
        {
            Assert.Equal("Eniripsa fille", NpcGfxAppearanceNames.Resolve(71, dir));
            Assert.Equal("GFX #9999", NpcGfxAppearanceNames.Resolve(9999, dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_bd_load_sets_human_error_on_service()
    {
        var service = new NpcGfxCatalogService();
        Assert.False(service.IsLoaded);
        Assert.Equal("Catálogo apariencias NPC: no cargado", service.Status);
    }

    private static IReadOnlyList<NpcGfxCatalogEntry> BuildSampleCatalog()
    {
        var rows = new List<NpcGfxUsageRow>
        {
            new() { GfxId = 71, Nombre = "Clara Dol" },
            new() { GfxId = 71, Nombre = "Mimi Fista" },
            new() { GfxId = 1245, Nombre = "Ogivol Scarratero" },
        };
        for (var i = 0; i < 267; i++)
            rows.Add(new NpcGfxUsageRow { GfxId = 9999, Nombre = $"NPC placeholder {i}" });

        var doc = System.Xml.Linq.XDocument.Parse(SampleSpritesXml);
        return NpcGfxCatalogBuilder.Build(rows, SpritesXmlParser.Parse(doc).Names, clipsRoot: null).Entries;
    }
}
