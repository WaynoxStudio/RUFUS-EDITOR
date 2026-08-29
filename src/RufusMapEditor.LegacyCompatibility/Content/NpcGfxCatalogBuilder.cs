using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>ADMIN.UI.4B.2A.3C — build confirmed NPC gfx catalog from BD rows + clips.</summary>
public static class NpcGfxCatalogBuilder
{
    public sealed class BuildResult
    {
        public required IReadOnlyList<NpcGfxCatalogEntry> Entries { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
    }

    public static BuildResult Build(
        IReadOnlyList<NpcGfxUsageRow> usageRows,
        IReadOnlyDictionary<int, string> spriteNames,
        string? clipsRoot,
        IEnumerable<string>? extraWarnings = null)
    {
        ArgumentNullException.ThrowIfNull(usageRows);
        ArgumentNullException.ThrowIfNull(spriteNames);

        var warnings = extraWarnings?.ToList() ?? new List<string>();
        var grouped = usageRows
            .GroupBy(r => r.GfxId)
            .OrderBy(g => g.Key)
            .ToList();

        var entries = new List<NpcGfxCatalogEntry>(grouped.Count);
        foreach (var group in grouped)
        {
            var gfxId = group.Key;
            var npcNames = group
                .Select(r => r.Nombre?.Trim() ?? "")
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            spriteNames.TryGetValue(gfxId, out var spriteName);
            var artRel = VisualClipPaths.ArtworkRelative(gfxId);
            var sprRel = VisualClipPaths.SpriteRelative(gfxId);
            var artFull = VisualClipPaths.ResolveFull(clipsRoot, artRel);
            var sprFull = VisualClipPaths.ResolveFull(clipsRoot, sprRel);

            entries.Add(new NpcGfxCatalogEntry
            {
                GfxId = gfxId,
                DisplayName = NpcGfxCatalogFormatting.FormatDisplayName(gfxId, spriteName),
                NpcNames = npcNames,
                NpcCount = group.Count(),
                HasSprite = VisualClipPaths.FileExists(sprFull),
                HasArtwork = VisualClipPaths.FileExists(artFull),
            });
        }

        return new BuildResult
        {
            Entries = entries,
            Warnings = warnings,
        };
    }
}
