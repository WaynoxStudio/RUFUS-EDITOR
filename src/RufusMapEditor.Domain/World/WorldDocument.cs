using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.Domain.World;

public sealed class WorldViewState
{
    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public bool MosaicMode { get; set; }
    public bool ShowInfoOverlay { get; set; } = true;
}

/// <summary>
/// RUFUS world composition: many maps on an X/Y grid plus unplaced local maps.
/// </summary>
public sealed class WorldDocument
{
    public string WorldId { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = "Nuevo mundo";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<WorldMapPlacement> Placements { get; set; } = new();
    public List<string> UnplacedDocumentKeys { get; set; } = new();

    /// <summary>Key → editable map document (deep-owned copies).</summary>
    public Dictionary<string, WorldMapEntry> Documents { get; set; } = new(StringComparer.Ordinal);

    public WorldViewState View { get; set; } = new();
    public string? FilePath { get; set; }
    public bool IsDirty { get; set; }

    /// <summary>Grid columns; 0 = dynamic canvas (no fixed grid).</summary>
    public int GridWidth { get; set; }

    /// <summary>Grid rows; 0 = dynamic canvas (no fixed grid).</summary>
    public int GridHeight { get; set; }

    /// <summary>World X of the top-left grid cell.</summary>
    public int OriginX { get; set; }

    /// <summary>World Y of the top-left grid cell.</summary>
    public int OriginY { get; set; }

    public bool HasGrid => GridWidth > 0 && GridHeight > 0;
}

public sealed class WorldMapEntry
{
    public required string Key { get; init; }
    public required MapDocument Document { get; set; }
    public WorldMapOrigin Origin { get; set; } = WorldMapOrigin.Library;
    public WorldMapPublicationState PublicationState { get; set; } = WorldMapPublicationState.FromLibrary;
    public int? SourceLibraryMapId { get; set; }
    public string? LinkedRufmapPath { get; set; }
}
