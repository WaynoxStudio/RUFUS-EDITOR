namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>ADMIN.UI.4B.2A.3C — one confirmed NPC look per distinct <c>npcs_modelo.gfxID</c>.</summary>
public sealed class NpcGfxCatalogEntry
{
    public required int GfxId { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> NpcNames { get; init; }
    public required int NpcCount { get; init; }
    public required bool HasSprite { get; init; }
    public required bool HasArtwork { get; init; }

    /// <summary>V1 catalog only includes gfx confirmed in npcs_modelo.</summary>
    public bool IsConfirmedNpcGfx => true;

    public string GfxIdLabel => $"GFX #{GfxId}";

    public string UsageSummary =>
        NpcCount == 1 ? "Usado por 1 NPC" : $"Usado por {NpcCount} NPC";

    public string UsageDetail => NpcGfxCatalogFormatting.FormatUsageDetail(NpcNames, NpcCount);
}

public static class NpcGfxCatalogFormatting
{
    public const int MaxNamesInline = 3;

    public static string FormatDisplayName(int gfxId, string? spriteXmlName) =>
        string.IsNullOrWhiteSpace(spriteXmlName) ? $"GFX #{gfxId}" : spriteXmlName.Trim();

    public static string FormatUsageDetail(IReadOnlyList<string> npcNames, int npcCount)
    {
        if (npcCount <= 0)
            return "";

        var names = npcNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return npcCount == 1 ? "Usado por 1 NPC" : $"Usado por {npcCount} NPC";

        if (names.Count <= MaxNamesInline && npcCount <= MaxNamesInline)
            return "Usado por: " + string.Join(", ", names);

        var preview = string.Join(", ", names.Take(MaxNamesInline));
        if (npcCount > names.Count || names.Count > MaxNamesInline)
            return $"Usado por {npcCount} NPC · {preview}…";

        return $"Usado por: {preview}";
    }
}
