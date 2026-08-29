using System.Globalization;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Database;

public static class RevisionLogic
{
    public static bool IsNumeric(string? fecha) =>
        !string.IsNullOrWhiteSpace(fecha)
        && int.TryParse(fecha.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static bool TryIncrement(string? current, out string next, out string? error)
    {
        next = "";
        error = null;
        var raw = current?.Trim() ?? "";
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            error = $"El valor de revisión actual no es numérico: {current ?? "(vacío)"}";
            return false;
        }

        // Preserve leading zeros / width (e.g. 0706141524 → 0706141525).
        var width = raw.Length;
        next = (n + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        return true;
    }
}

public sealed class SchemaCheckResult
{
    public required bool Ok { get; init; }
    public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
    public string? Message { get; init; }
}

public sealed class FieldDiff
{
    public required string Label { get; init; }
    public required string Before { get; init; }
    public required string After { get; init; }
    public bool Changed => !string.Equals(Before, After, StringComparison.Ordinal);
}

public sealed class PublishDiff
{
    public required int MapId { get; init; }
    public required FieldDiff Revision { get; init; }
    public required FieldDiff MapData { get; init; }
    public required FieldDiff FightPlaces { get; init; }
    public required FieldDiff Width { get; init; }
    public required FieldDiff Height { get; init; }
    public required FieldDiff Background { get; init; }
    public required FieldDiff Music { get; init; }
    public required FieldDiff Ambiance { get; init; }
    public required FieldDiff Outdoor { get; init; }
    public required FieldDiff Capabilities { get; init; }
    public required FieldDiff WorldX { get; init; }
    public required FieldDiff WorldY { get; init; }

    public IEnumerable<FieldDiff> Enumerate()
    {
        yield return Revision;
        yield return MapData;
        yield return FightPlaces;
        yield return Width;
        yield return Height;
        yield return Background;
        yield return Music;
        yield return Ambiance;
        yield return Outdoor;
        yield return Capabilities;
        yield return WorldX;
        yield return WorldY;
    }

    public bool HasContentChange =>
        Enumerate().Any(f => f.Label != "Revisión" && f.Changed);
}

/// <summary>
/// Effective publish payload for an existing row: undefined editor fields fall back to BD
/// and are omitted from UPDATE unless explicitly defined/edited.
/// </summary>
public sealed class ResolvedPublishState
{
    public required int Ancho { get; init; }
    public required int Alto { get; init; }
    public required int BgId { get; init; }
    public required int MusicId { get; init; }
    public required int AmbienteId { get; init; }
    public required int OutDoor { get; init; }
    public required int Capabilities { get; init; }
    public required string PosPelea { get; init; }
    public required string MapData { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }

    public required bool IncludeBgId { get; init; }
    public required bool IncludeMusicId { get; init; }
    public required bool IncludeAmbienteId { get; init; }
    public required bool IncludeOutDoor { get; init; }
    public required bool IncludeCapabilities { get; init; }
    public required bool IncludeX { get; init; }
    public required bool IncludeY { get; init; }

    public IReadOnlyList<string> ColumnsToUpdate
    {
        get
        {
            var cols = new List<string>
            {
                MapasColumns.Fecha,
                MapasColumns.Ancho,
                MapasColumns.Alto,
                MapasColumns.PosPelea,
                MapasColumns.MapData,
            };
            if (IncludeBgId) cols.Add(MapasColumns.BgId);
            if (IncludeMusicId) cols.Add(MapasColumns.MusicId);
            if (IncludeAmbienteId) cols.Add(MapasColumns.AmbienteId);
            if (IncludeOutDoor) cols.Add(MapasColumns.OutDoor);
            if (IncludeCapabilities) cols.Add(MapasColumns.Capabilities);
            if (IncludeX) cols.Add(MapasColumns.X);
            if (IncludeY) cols.Add(MapasColumns.Y);
            return cols;
        }
    }
}

public static class MapPublishLogic
{
    public static SchemaCheckResult CheckSchema(IEnumerable<string> columns)
    {
        var set = new HashSet<string>(columns.Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
        var missing = MapasColumns.Required.Where(r => !set.Contains(r)).ToList();
        return new SchemaCheckResult
        {
            Ok = missing.Count == 0,
            Missing = missing,
            Message = missing.Count == 0 ? null : "Faltan columnas: " + string.Join(", ", missing),
        };
    }

    /// <summary>
    /// Copies BD metadata into the document. Never touches MapData.
    /// FightPlaces is left unchanged (editor-owned; do not overwrite user edits).
    /// </summary>
    public static DatabaseMapSnapshot SyncMetadataFromDatabase(MapDocument map, MapasRow row)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(row);

        var snap = DatabaseMapSnapshot.FromRow(row);
        map.BackgroundId = row.BgId;
        map.BackgroundDefined = true;
        map.MusicId = row.MusicId;
        map.MusicDefined = true;
        map.AmbianceId = row.AmbienteId;
        map.AmbianceDefined = true;
        map.Outdoor = row.OutDoor != 0;
        map.Capabilities = row.Capabilities;
        map.CapabilitiesDefined = true;
        map.WorldX = row.X;
        map.WorldY = row.Y;
        map.WorldCoordinatesSet = true;
        // DateMap / revision display: show BD revision without forcing a publish.
        map.DateMap = row.Fecha ?? map.DateMap;
        return snap;
    }

    /// <summary>
    /// Resolves editor values against an existing BD row.
    /// Undefined metadata uses BD as baseline and is excluded from UPDATE.
    /// </summary>
    public static ResolvedPublishState ResolveForExisting(MapDocument map, MapasRow db)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(db);
        MapCellEditor.SyncDocument(map);

        return new ResolvedPublishState
        {
            Ancho = map.Width,
            Alto = map.Height,
            BgId = map.BackgroundDefined ? map.BackgroundId : db.BgId,
            MusicId = map.MusicDefined ? map.MusicId : db.MusicId,
            AmbienteId = map.AmbianceDefined ? map.AmbianceId : db.AmbienteId,
            OutDoor = map.Outdoor is null ? db.OutDoor : (map.Outdoor.Value ? 1 : 0),
            Capabilities = map.CapabilitiesDefined ? map.Capabilities : db.Capabilities,
            PosPelea = map.FightPlaces ?? "",
            MapData = map.MapData ?? "",
            X = map.WorldCoordinatesSet ? map.WorldX : db.X,
            Y = map.WorldCoordinatesSet ? map.WorldY : db.Y,
            IncludeBgId = map.BackgroundDefined,
            IncludeMusicId = map.MusicDefined,
            IncludeAmbienteId = map.AmbianceDefined,
            IncludeOutDoor = map.Outdoor is not null,
            IncludeCapabilities = map.CapabilitiesDefined,
            IncludeX = map.WorldCoordinatesSet,
            IncludeY = map.WorldCoordinatesSet,
        };
    }

    /// <summary>INSERT / full publish path: every editor field must be reliable.</summary>
    public static MapPublishValues FromDocument(MapDocument map, string newFecha)
    {
        ArgumentNullException.ThrowIfNull(map);
        MapCellEditor.SyncDocument(map);
        if (map.Outdoor is null)
            throw new InvalidOperationException("Outdoor (outDoor) no está definido; no se inventará.");
        if (!map.WorldCoordinatesSet)
            throw new InvalidOperationException(
                "Coordenada X/Y no definidas (WorldCoordinatesSet=false). " +
                "0,0 es válido solo si se definió explícitamente.");

        return new MapPublishValues
        {
            Id = map.Id,
            Fecha = newFecha,
            Ancho = map.Width,
            Alto = map.Height,
            BgId = map.BackgroundId,
            MusicId = map.MusicId,
            AmbienteId = map.AmbianceId,
            OutDoor = map.Outdoor.Value ? 1 : 0,
            Capabilities = map.Capabilities,
            PosPelea = map.FightPlaces ?? "",
            MapData = map.MapData ?? "",
            X = map.WorldX,
            Y = map.WorldY,
            ColumnsToUpdate = MapasColumns.Updated.ToArray(),
        };
    }

    public static MapPublishValues FromResolved(MapDocument map, MapasRow db, string newFecha)
    {
        var r = ResolveForExisting(map, db);
        return new MapPublishValues
        {
            Id = map.Id,
            Fecha = newFecha,
            Ancho = r.Ancho,
            Alto = r.Alto,
            BgId = r.BgId,
            MusicId = r.MusicId,
            AmbienteId = r.AmbienteId,
            OutDoor = r.OutDoor,
            Capabilities = r.Capabilities,
            PosPelea = r.PosPelea,
            MapData = r.MapData,
            X = r.X,
            Y = r.Y,
            ColumnsToUpdate = r.ColumnsToUpdate,
        };
    }

    public static bool ContentMatchesDb(MapasRow db, MapDocument map)
    {
        var r = ResolveForExisting(map, db);
        return db.Ancho == r.Ancho
            && db.Alto == r.Alto
            && db.BgId == r.BgId
            && db.MusicId == r.MusicId
            && db.AmbienteId == r.AmbienteId
            && db.OutDoor == r.OutDoor
            && db.Capabilities == r.Capabilities
            && string.Equals(db.PosPelea, r.PosPelea, StringComparison.Ordinal)
            && string.Equals(db.MapData, r.MapData, StringComparison.Ordinal)
            && db.X == r.X
            && db.Y == r.Y;
    }

    public static PublishDiff BuildDiff(MapasRow db, MapDocument map, string proposedFecha)
    {
        var r = ResolveForExisting(map, db);
        var mdSame = string.Equals(db.MapData, r.MapData, StringComparison.Ordinal);
        var fpSame = string.Equals(db.PosPelea, r.PosPelea, StringComparison.Ordinal);
        string S(int v) => v.ToString(CultureInfo.InvariantCulture);

        return new PublishDiff
        {
            MapId = db.Id,
            Revision = F("Revisión", db.Fecha, proposedFecha),
            MapData = F("MapData",
                mdSame ? "SIN CAMBIOS" : $"len {db.MapData.Length}",
                mdSame ? "SIN CAMBIOS" : $"MODIFICADO (len {db.MapData.Length} → {r.MapData.Length})"),
            FightPlaces = F("Posiciones combate",
                fpSame ? "SIN CAMBIOS" : "actual",
                fpSame ? "SIN CAMBIOS" : "MODIFICADO"),
            Width = F("Ancho", S(db.Ancho), S(r.Ancho)),
            Height = F("Alto", S(db.Alto), S(r.Alto)),
            Background = F("Background", S(db.BgId), S(r.BgId)),
            Music = F("Música", S(db.MusicId), S(r.MusicId)),
            Ambiance = F("Ambiente", S(db.AmbienteId), S(r.AmbienteId)),
            Outdoor = F("Outdoor", S(db.OutDoor), S(r.OutDoor)),
            Capabilities = F("Capabilities", S(db.Capabilities), S(r.Capabilities)),
            WorldX = F("X", S(db.X), S(r.X)),
            WorldY = F("Y", S(db.Y), S(r.Y)),
        };
    }

    private static FieldDiff F(string label, string before, string after) => new()
    {
        Label = label,
        Before = before,
        After = after,
    };
}
