namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.2 — one row per Item ID (unic). Item ID ≠ GFX.</summary>
public sealed class ItemCatalogEntry
{
    public required int ItemId { get; init; }
    public required string Nombre { get; init; }
    public required int Level { get; init; }
    public required int TypeId { get; init; }
    public required string Category { get; init; }
    public required int GfxId { get; init; }
    public required string IconRelativePath { get; init; }
    public string? IconFullPath { get; init; }
    public bool IconExists { get; init; }
}
