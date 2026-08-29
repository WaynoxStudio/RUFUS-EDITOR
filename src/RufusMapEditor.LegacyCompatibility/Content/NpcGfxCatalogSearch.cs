using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>ADMIN.UI.4B.2A.3C — search confirmed NPC gfx catalog.</summary>
public static class NpcGfxCatalogSearch
{
    public static IReadOnlyList<NpcGfxCatalogEntry> Filter(
        IReadOnlyList<NpcGfxCatalogEntry> source,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(source);
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return source;

        var list = new List<NpcGfxCatalogEntry>();
        foreach (var entry in source)
        {
            if (Matches(entry, q))
                list.Add(entry);
        }

        return list;
    }

    public static bool Matches(NpcGfxCatalogEntry entry, string query)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return true;

        if (int.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)
            && entry.GfxId == num)
            return true;

        if (entry.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var name in entry.NpcNames)
        {
            if (name.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
