using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Database;

/// <summary>
/// UPDATE path that writes only <c>X</c>/<c>Y</c> — never mapData, fecha, or other columns.
/// </summary>
public static class MapCoordinatesUpdateLogic
{
    public static readonly string[] ColumnsToUpdate =
    [
        MapasColumns.X,
        MapasColumns.Y,
    ];

    public static bool TryBuildValues(MapDocument map, MapasRow db, out MapPublishValues values, out string? error)
    {
        values = null!;
        error = null;
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(db);

        if (map.Id <= 0 || map.Id != db.Id)
        {
            error = $"MapId inválido o distinto de BD (editor={map.Id}, BD={db.Id}).";
            return false;
        }

        if (!map.WorldCoordinatesSet)
        {
            error = $"Mapa {map.Id}: coordenadas BD no definidas.";
            return false;
        }

        // Fill non-updated fields from BD so MysqlMapasRepository verification stays consistent.
        values = new MapPublishValues
        {
            Id = map.Id,
            Fecha = db.Fecha,
            Ancho = db.Ancho,
            Alto = db.Alto,
            BgId = db.BgId,
            MusicId = db.MusicId,
            AmbienteId = db.AmbienteId,
            OutDoor = db.OutDoor,
            Capabilities = db.Capabilities,
            PosPelea = db.PosPelea ?? "",
            MapData = db.MapData ?? "",
            X = map.WorldX,
            Y = map.WorldY,
            ColumnsToUpdate = ColumnsToUpdate,
        };
        return true;
    }
}
