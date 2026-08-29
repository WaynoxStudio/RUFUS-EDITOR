namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.2 — one row per <c>mobs_modelo.id</c> (never merge by name/gfx).</summary>
public sealed class MonsterCatalogEntry
{
    public required int Id { get; init; }
    public required string Nombre { get; init; }
    public required int GfxId { get; init; }
    public required IReadOnlyList<int> Levels { get; init; }
    public required string ArtworkRelativePath { get; init; }
    public required string SpriteRelativePath { get; init; }
    public string? ArtworkFullPath { get; init; }
    public string? SpriteFullPath { get; init; }
    public bool ArtworkExists { get; init; }
    public bool SpriteExists { get; init; }

    public string LevelsDisplay =>
        Levels.Count == 0 ? "—" : string.Join(" / ", Levels);
}
