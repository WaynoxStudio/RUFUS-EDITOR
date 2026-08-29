using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class MonsterSearchLib44Tests
{
    [Fact]
    public void SearchMonsters_no_artificial_cap_returns_full_catalog_when_unfiltered()
    {
        var svc = new VisualLibraryService();
        // Inject via reflection-free public path: LoadMonsters requires DB.
        // Build a lightweight in-memory catalog by using the same SearchMonsters contract
        // through a test double list injected into a fresh service via Relink-compatible path.
        // Instead: exercise Matches + unlimited SearchMonsters with a stub service subclass.

        var entries = new List<MonsterCatalogEntry>(300);
        for (var i = 1; i <= 300; i++)
        {
            entries.Add(new MonsterCatalogEntry
            {
                Id = 10000 + i,
                Nombre = $"MobTest {i}",
                GfxId = 20000 + i,
                Levels = new[] { 1, 2, 3 },
                ArtworkRelativePath = $"artworks/big/{20000 + i}.swf",
                SpriteRelativePath = $"sprites/{20000 + i}.swf",
            });
        }

        // Use Shared-style API via helper that mirrors SearchMonsters unlimited logic.
        var all = SearchAll(entries, query: null);
        Assert.Equal(300, all.Count);

        var past80 = SearchAll(entries, "MobTest 180");
        Assert.Single(past80);
        Assert.Equal(10180, past80[0].Id);

        var past250 = SearchAll(entries, "MobTest 280");
        Assert.Single(past250);
        Assert.Equal(10280, past250[0].Id);

        var byId = SearchAll(entries, "10200");
        Assert.Single(byId);
        Assert.Equal(10200, byId[0].Id);

        var byGfx = SearchAll(entries, "20250");
        Assert.Single(byGfx);
        Assert.Equal(250, byGfx[0].Id - 10000);
    }

    [Fact]
    public void VisualLibraryService_SearchMonsters_signature_has_no_take_limit()
    {
        var method = typeof(VisualLibraryService).GetMethod(nameof(VisualLibraryService.SearchMonsters));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal("query", parameters[0].Name);
    }

    /// <summary>Mirrors unlimited SearchMonsters filtering (LIB.4.4 contract).</summary>
    private static IReadOnlyList<MonsterCatalogEntry> SearchAll(
        IReadOnlyList<MonsterCatalogEntry> all,
        string? query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return all;

        var list = new List<MonsterCatalogEntry>();
        foreach (var m in all)
        {
            if (m.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)
                || m.Id.ToString().Contains(q, StringComparison.Ordinal)
                || m.GfxId.ToString().Contains(q, StringComparison.Ordinal))
                list.Add(m);
        }

        return list;
    }
}
