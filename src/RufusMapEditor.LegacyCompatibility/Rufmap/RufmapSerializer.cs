using System.Text.Json;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Rufmap;

public enum RufmapLoadErrorKind
{
    None = 0,
    CorruptJson,
    UnsupportedFutureVersion,
    UnsupportedPastVersion,
    MissingRequiredData,
    InconsistentGeometry,
    MapDataMismatch,
}

public sealed class RufmapException : Exception
{
    public RufmapLoadErrorKind Kind { get; }

    public RufmapException(RufmapLoadErrorKind kind, string message) : base(message)
    {
        Kind = kind;
    }
}

public sealed class RufmapLoadResult
{
    public required MapDocument Document { get; init; }
    public required RufmapFileDto File { get; init; }
}

/// <summary>
/// Versioned .rufmap serializer. Cells are canonical; MapData is an integrity reference.
/// </summary>
public static class RufmapSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static RufmapFileDto FromDocument(
        MapDocument map,
        string documentId,
        DateTimeOffset createdUtc,
        RufmapSourceDto? source,
        string? projectName = null,
        string? comment = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("documentId required", nameof(documentId));

        MapCellEditor.SyncMapDataString(map);
        var expectedCount = MapGeometry.CellCount(map.Width, map.Height);
        if (map.Cells.Count != expectedCount)
            throw new RufmapException(
                RufmapLoadErrorKind.InconsistentGeometry,
                $"Cell count {map.Cells.Count} != expected {expectedCount} for {map.Width}x{map.Height}.");

        var dto = new RufmapFileDto
        {
            FormatVersion = RufmapFormat.CurrentVersion,
            DocumentId = documentId,
            CreatedUtc = createdUtc,
            ModifiedUtc = DateTimeOffset.UtcNow,
            ProjectName = projectName,
            Comment = comment,
            Source = source,
            Map = new RufmapMapDto
            {
                Id = map.Id,
                Width = map.Width,
                Height = map.Height,
                DateMap = map.DateMap ?? "",
                Key = map.Key ?? "",
                FightPlaces = map.FightPlaces ?? "",
                BackgroundId = map.BackgroundId,
                BackgroundDefined = map.BackgroundDefined,
                MusicId = map.MusicId,
                MusicDefined = map.MusicDefined,
                AmbianceId = map.AmbianceId,
                AmbianceDefined = map.AmbianceDefined,
                Capabilities = map.Capabilities,
                CapabilitiesDefined = map.CapabilitiesDefined,
                Outdoor = map.Outdoor,
                WorldX = map.WorldX,
                WorldY = map.WorldY,
                WorldCoordinatesSet = map.WorldCoordinatesSet,
                MapData = map.MapData,
                Cells = map.Cells.Select(ToDto).ToList(),
            },
        };

        return dto;
    }

    public static string Serialize(RufmapFileDto file) =>
        JsonSerializer.Serialize(file, JsonOptions);

    public static RufmapFileDto DeserializeDto(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new RufmapException(RufmapLoadErrorKind.CorruptJson, "El archivo está vacío.");

        RufmapFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RufmapFileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new RufmapException(RufmapLoadErrorKind.CorruptJson, $"JSON inválido: {ex.Message}");
        }

        if (dto is null)
            throw new RufmapException(RufmapLoadErrorKind.CorruptJson, "No se pudo interpretar el proyecto.");

        ValidateHeader(dto);
        return RufmapMigrator.MigrateToCurrent(dto);
    }

    public static RufmapLoadResult ToDocument(RufmapFileDto dto)
    {
        ValidateHeader(dto);
        var m = dto.Map ?? throw new RufmapException(RufmapLoadErrorKind.MissingRequiredData, "Falta el bloque map.");

        if (m.Width <= 0 || m.Height <= 0)
            throw new RufmapException(RufmapLoadErrorKind.MissingRequiredData, "Width/Height inválidos.");

        if (m.Cells is null || m.Cells.Count == 0)
            throw new RufmapException(RufmapLoadErrorKind.MissingRequiredData, "El proyecto no contiene celdas.");

        var expected = MapGeometry.CellCount(m.Width, m.Height);
        if (m.Cells.Count != expected)
            throw new RufmapException(
                RufmapLoadErrorKind.InconsistentGeometry,
                $"Celdas {m.Cells.Count} no coinciden con {m.Width}x{m.Height} (esperado {expected}).");

        var cells = m.Cells.Select(FromDto).ToList();
        var encoded = MapDataCodec.EncodeMap(cells);

        if (!string.IsNullOrEmpty(m.MapData) && !string.Equals(m.MapData, encoded, StringComparison.Ordinal))
            throw new RufmapException(
                RufmapLoadErrorKind.MapDataMismatch,
                "El MapData del archivo no coincide con las celdas serializadas (posible corrupción).");

        var doc = new MapDocument
        {
            Id = m.Id,
            Width = m.Width,
            Height = m.Height,
            DateMap = m.DateMap ?? "AME",
            Key = m.Key ?? "",
            FightPlaces = m.FightPlaces ?? "",
            BackgroundId = m.BackgroundId,
            BackgroundDefined = m.BackgroundDefined,
            MusicId = m.MusicId,
            MusicDefined = m.MusicDefined,
            AmbianceId = m.AmbianceId,
            AmbianceDefined = m.AmbianceDefined,
            Capabilities = m.Capabilities,
            CapabilitiesDefined = m.CapabilitiesDefined,
            Outdoor = m.Outdoor,
            WorldX = m.WorldX,
            WorldY = m.WorldY,
            WorldCoordinatesSet = m.WorldCoordinatesSet,
            Cells = cells,
            MapData = encoded,
        };

        return new RufmapLoadResult { Document = doc, File = dto };
    }

    public static RufmapLoadResult LoadFromJson(string json) =>
        ToDocument(DeserializeDto(json));

    private static void ValidateHeader(RufmapFileDto dto)
    {
        if (dto.FormatVersion <= 0)
            throw new RufmapException(RufmapLoadErrorKind.MissingRequiredData, "formatVersion ausente o inválido.");

        if (dto.FormatVersion > RufmapFormat.CurrentVersion)
            throw new RufmapException(
                RufmapLoadErrorKind.UnsupportedFutureVersion,
                $"Este proyecto fue creado con una versión de formato más reciente (formatVersion {dto.FormatVersion}) " +
                $"y no puede abrirse de forma segura. Esta aplicación soporta hasta v{RufmapFormat.CurrentVersion}.");

        if (dto.FormatVersion < 1)
            throw new RufmapException(
                RufmapLoadErrorKind.UnsupportedPastVersion,
                $"formatVersion {dto.FormatVersion} no está soportado.");

        if (string.IsNullOrWhiteSpace(dto.DocumentId))
            throw new RufmapException(RufmapLoadErrorKind.MissingRequiredData, "documentId ausente.");
    }

    private static RufmapCellDto ToDto(CellData c) => new()
    {
        Active = c.Active,
        LineOfSight = c.LineOfSight,
        Movement = (int)c.Movement,
        GroundGfxId = c.GroundGfxId,
        Object1GfxId = c.Object1GfxId,
        Object2GfxId = c.Object2GfxId,
        FlipGround = c.FlipGround,
        FlipObject1 = c.FlipObject1,
        FlipObject2 = c.FlipObject2,
        GroundRotation = c.GroundRotation,
        Object1Rotation = c.Object1Rotation,
        GroundLevel = c.GroundLevel,
        GroundSlope = c.GroundSlope,
        InteractiveObject = c.InteractiveObject,
    };

    private static CellData FromDto(RufmapCellDto c) => new()
    {
        Active = c.Active,
        LineOfSight = c.LineOfSight,
        Movement = (MovementType)(c.Movement & 7),
        GroundGfxId = c.GroundGfxId,
        Object1GfxId = c.Object1GfxId,
        Object2GfxId = c.Object2GfxId,
        FlipGround = c.FlipGround,
        FlipObject1 = c.FlipObject1,
        FlipObject2 = c.FlipObject2,
        GroundRotation = Math.Clamp(c.GroundRotation, 0, 3),
        Object1Rotation = Math.Clamp(c.Object1Rotation, 0, 3),
        GroundLevel = Math.Clamp(c.GroundLevel, 0, 15),
        GroundSlope = Math.Clamp(c.GroundSlope, 0, 15),
        InteractiveObject = c.InteractiveObject,
    };
}
