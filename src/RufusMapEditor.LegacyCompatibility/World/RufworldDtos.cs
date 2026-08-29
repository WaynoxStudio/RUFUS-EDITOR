using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.World;

public static class RufworldFormat
{
    public const int CurrentVersion = 1;
    public const string FileExtension = ".rufworld";
}

public sealed class RufworldFileDto
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = RufworldFormat.CurrentVersion;

    [JsonPropertyName("worldId")]
    public string WorldId { get; set; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Nuevo mundo";

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; set; }

    [JsonPropertyName("modifiedUtc")]
    public DateTimeOffset ModifiedUtc { get; set; }

    [JsonPropertyName("placements")]
    public List<RufworldPlacementDto> Placements { get; set; } = new();

    [JsonPropertyName("unplacedKeys")]
    public List<string> UnplacedKeys { get; set; } = new();

    [JsonPropertyName("documents")]
    public Dictionary<string, RufworldDocumentDto> Documents { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("view")]
    public RufworldViewDto? View { get; set; }

    [JsonPropertyName("gridWidth")]
    public int GridWidth { get; set; }

    [JsonPropertyName("gridHeight")]
    public int GridHeight { get; set; }

    [JsonPropertyName("originX")]
    public int OriginX { get; set; }

    [JsonPropertyName("originY")]
    public int OriginY { get; set; }
}

public sealed class RufworldPlacementDto
{
    [JsonPropertyName("documentKey")]
    public string DocumentKey { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

public sealed class RufworldDocumentDto
{
    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "Library";

    [JsonPropertyName("publicationState")]
    public string PublicationState { get; set; } = "FromLibrary";

    [JsonPropertyName("sourceLibraryMapId")]
    public int? SourceLibraryMapId { get; set; }

    [JsonPropertyName("linkedRufmapPath")]
    public string? LinkedRufmapPath { get; set; }

    [JsonPropertyName("map")]
    public Rufmap.RufmapMapDto Map { get; set; } = new();
}

public sealed class RufworldViewDto
{
    [JsonPropertyName("zoom")]
    public double Zoom { get; set; } = 1.0;

    [JsonPropertyName("panX")]
    public double PanX { get; set; }

    [JsonPropertyName("panY")]
    public double PanY { get; set; }

    [JsonPropertyName("mosaicMode")]
    public bool MosaicMode { get; set; }

    [JsonPropertyName("showInfoOverlay")]
    public bool ShowInfoOverlay { get; set; } = true;
}
