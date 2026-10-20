using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Database;

/// <summary>
/// Fast UPDATE path: <c>mapData</c> + <c>bgID</c> + <c>X</c>/<c>Y</c> + <c>fecha</c> (+1).
/// Never writes <c>ancho</c>/<c>alto</c> or <c>posPelea</c>.
/// </summary>
public static class MapQuickUpdateLogic
{
    public static readonly string[] ColumnsToUpdate =
    [
        MapasColumns.Fecha,
        MapasColumns.BgId,
        MapasColumns.MapData,
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

        MapCellEditor.SyncDocument(map);

        if (map.Width != db.Ancho || map.Height != db.Alto)
        {
            error =
                $"No se puede actualizar: el tamaño del editor ({map.Width}×{map.Height}) " +
                $"no coincide con BD ({db.Ancho}×{db.Alto}). Ancho/alto no se modifican.";
            return false;
        }

        if (!RevisionLogic.TryIncrement(db.Fecha, out var newFecha, out var revError))
        {
            error = revError ?? "No se pudo incrementar la fecha/revisión.";
            return false;
        }

        if (string.IsNullOrEmpty(map.MapData))
        {
            error = "mapData vacío; no se actualizará la BD.";
            return false;
        }

        var bgId = map.BackgroundDefined ? map.BackgroundId : db.BgId;
        var x = map.WorldCoordinatesSet ? map.WorldX : db.X;
        var y = map.WorldCoordinatesSet ? map.WorldY : db.Y;

        // Fill non-updated fields from BD so MysqlMapasRepository verification stays consistent.
        values = new MapPublishValues
        {
            Id = map.Id,
            Fecha = newFecha,
            Ancho = db.Ancho,
            Alto = db.Alto,
            BgId = bgId,
            MusicId = db.MusicId,
            AmbienteId = db.AmbienteId,
            OutDoor = db.OutDoor,
            Capabilities = db.Capabilities,
            PosPelea = db.PosPelea ?? "",
            MapData = map.MapData,
            X = x,
            Y = y,
            ColumnsToUpdate = ColumnsToUpdate,
        };
        return true;
    }
}
