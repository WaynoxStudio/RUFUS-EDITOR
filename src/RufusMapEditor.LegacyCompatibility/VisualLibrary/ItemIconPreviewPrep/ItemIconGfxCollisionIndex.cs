namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>Runtime SAFE vs COLLISION classification by distinct resolved SWF paths per GfxId.</summary>
public sealed class ItemIconGfxCollisionIndex
{
    private readonly HashSet<int> _safeGfxIds;

    private ItemIconGfxCollisionIndex(HashSet<int> safeGfxIds) =>
        _safeGfxIds = safeGfxIds;

    public bool IsSafeGfx(int gfxId) => gfxId > 0 && _safeGfxIds.Contains(gfxId);

    public static ItemIconGfxCollisionIndex Build(
        IEnumerable<(int GfxId, int TypeId)> items,
        string? clipsRoot)
    {
        var safe = new HashSet<int>();
        var groups = items
            .Where(x => x.GfxId > 0)
            .GroupBy(x => x.GfxId);

        foreach (var g in groups)
        {
            var distinctSwfs = g
                .Select(x => VisualClipPaths.ResolveItemIcon(clipsRoot, x.GfxId, x.TypeId))
                .Where(r => r.exists)
                .Select(r => r.relative)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (distinctSwfs <= 1)
                safe.Add(g.Key);
        }

        return new ItemIconGfxCollisionIndex(safe);
    }

    public static ItemIconGfxCollisionIndex BuildFromCatalog(
        IReadOnlyList<ItemIconPreviewSample> catalog,
        string? clipsRoot) =>
        Build(catalog.Select(x => (x.GfxId, x.TypeId)), clipsRoot);

    public static ItemIconGfxCollisionIndex BuildFromEntries(
        IEnumerable<ItemCatalogEntry> entries,
        string? clipsRoot) =>
        Build(entries.Select(x => (x.GfxId, x.TypeId)), clipsRoot);
}
