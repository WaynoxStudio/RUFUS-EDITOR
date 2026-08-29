using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Rufmap;

/// <summary>Current on-disk format version for .rufmap.</summary>
public static class RufmapFormat
{
    public const int CurrentVersion = 1;
    public const string FileExtension = ".rufmap";
    public const string MediaTypeHint = "application/x-rufus-map+json";
}

public sealed class RufmapFileDto
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = RufmapFormat.CurrentVersion;

    /// <summary>Stable project identity (not Map ID).</summary>
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; set; }

    [JsonPropertyName("modifiedUtc")]
    public DateTimeOffset ModifiedUtc { get; set; }

    [JsonPropertyName("projectName")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("source")]
    public RufmapSourceDto? Source { get; set; }

    [JsonPropertyName("map")]
    public RufmapMapDto Map { get; set; } = new();
}

public sealed class RufmapSourceDto
{
    /// <summary>e.g. LegacyAstria, RufmapNative</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "LegacyAstria";

    [JsonPropertyName("originalMapId")]
    public int? OriginalMapId { get; set; }

    /// <summary>Historical hint only — not required to open the project.</summary>
    [JsonPropertyName("libraryPathHint")]
    public string? LibraryPathHint { get; set; }
}

public sealed class RufmapMapDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("dateMap")]
    public string DateMap { get; set; } = "AME";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("fightPlaces")]
    public string FightPlaces { get; set; } = "";

    [JsonPropertyName("backgroundId")]
    public int BackgroundId { get; set; }

    [JsonPropertyName("musicId")]
    public int MusicId { get; set; }

    [JsonPropertyName("ambianceId")]
    public int AmbianceId { get; set; }

    [JsonPropertyName("capabilities")]
    public int Capabilities { get; set; }

    [JsonPropertyName("outdoor")]
    public bool? Outdoor { get; set; }

    /// <summary>World coordinate X → BD <c>mapas.X</c>.</summary>
    
    [JsonPropertyName("backgroundDefined")]
    public bool BackgroundDefined { get; set; }

    [JsonPropertyName("musicDefined")]
    public bool MusicDefined { get; set; }

    [JsonPropertyName("ambianceDefined")]
    public bool AmbianceDefined { get; set; }

    [JsonPropertyName("capabilitiesDefined")]
    public bool CapabilitiesDefined { get; set; }
    /// <summary>World coordinate X → BD <c>mapas.X</c>. 0 is valid when set.</summary>
    [JsonPropertyName("worldX")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int WorldX { get; set; }

    /// <summary>World coordinate Y → BD <c>mapas.Y</c>. 0 is valid when set.</summary>
    [JsonPropertyName("worldY")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int WorldY { get; set; }

    /// <summary>False = undefined (legacy). True = explicit coords, including (0,0).</summary>
    [JsonPropertyName("worldCoordinatesSet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool WorldCoordinatesSet { get; set; }

    /// <summary>
    /// Canonical editable cells (all MapData-backed fields).
    /// </summary>
    [JsonPropertyName("cells")]
    public List<RufmapCellDto> Cells { get; set; } = new();

    /// <summary>
    /// Encoded MapData string at save time — integrity reference (must match Encode(cells)).
    /// Derived on load from cells if present; verified when both exist.
    /// </summary>
    [JsonPropertyName("mapData")]
    public string MapData { get; set; } = "";
}

public sealed class RufmapCellDto
{
    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("los")]
    public bool LineOfSight { get; set; } = true;

    [JsonPropertyName("movement")]
    public int Movement { get; set; }

    [JsonPropertyName("ground")]
    public int GroundGfxId { get; set; }

    [JsonPropertyName("object1")]
    public int Object1GfxId { get; set; }

    [JsonPropertyName("object2")]
    public int Object2GfxId { get; set; }

    [JsonPropertyName("flipG")]
    public bool FlipGround { get; set; }

    [JsonPropertyName("flipO1")]
    public bool FlipObject1 { get; set; }

    [JsonPropertyName("flipO2")]
    public bool FlipObject2 { get; set; }

    [JsonPropertyName("rotG")]
    public int GroundRotation { get; set; }

    [JsonPropertyName("rotO1")]
    public int Object1Rotation { get; set; }

    [JsonPropertyName("level")]
    public int GroundLevel { get; set; } = 7;

    [JsonPropertyName("slope")]
    public int GroundSlope { get; set; } = 1;

    [JsonPropertyName("io")]
    public bool InteractiveObject { get; set; }
}
