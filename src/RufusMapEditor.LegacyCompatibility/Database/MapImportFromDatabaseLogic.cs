using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Database;

/// <summary>
/// Builds an editable <see cref="MapDocument"/> from a single <c>estaticos.mapas</c> row.
/// Imports <c>mapData</c> (+ dimensions / useful metadata). Does not import <c>posPelea</c>.
/// </summary>
public static class MapImportFromDatabaseLogic
{
    public static bool TryCreateDocument(MapasRow row, out MapDocument document, out string? error)
    {
        document = null!;
        error = null;

        if (row is null)
        {
            error = "Fila BD nula.";
            return false;
        }

        if (row.Id <= 0)
        {
            error = $"Id de mapa inválido: {row.Id}.";
            return false;
        }

        if (row.Ancho < 1 || row.Alto < 1)
        {
            error = $"Dimensiones inválidas (ancho={row.Ancho}, alto={row.Alto}).";
            return false;
        }

        var mapData = row.MapData ?? "";
        if (string.IsNullOrWhiteSpace(mapData))
        {
            error = $"El mapa {row.Id} no tiene mapData en BD.";
            return false;
        }

        if (mapData.Length % MapDataConstants.CharsPerCell != 0)
        {
            error =
                $"mapData del mapa {row.Id} tiene longitud {mapData.Length}, " +
                $"que no es múltiplo de {MapDataConstants.CharsPerCell}.";
            return false;
        }

        var expectedCells = MapGeometry.CellCount(row.Ancho, row.Alto);
        var actualCells = mapData.Length / MapDataConstants.CharsPerCell;
        if (expectedCells != actualCells)
        {
            error =
                $"mapData del mapa {row.Id} no coincide con ancho×alto: " +
                $"esperadas {expectedCells} celdas ({row.Ancho}×{row.Alto}), hay {actualCells}.";
            return false;
        }

        CellData[] cells;
        try
        {
            cells = MapDataCodec.DecodeMap(mapData);
        }
        catch (Exception ex)
        {
            error = $"No se pudo decodificar mapData del mapa {row.Id}: {ex.Message}";
            return false;
        }

        document = new MapDocument
        {
            Id = row.Id,
            Width = row.Ancho,
            Height = row.Alto,
            MapData = mapData,
            DateMap = string.IsNullOrWhiteSpace(row.Fecha) ? "AME" : row.Fecha,
            Key = row.Key ?? "",
            // User request: only mapData for now — do not import posPelea.
            FightPlaces = "",
            BackgroundId = row.BgId,
            BackgroundDefined = true,
            MusicId = row.MusicId,
            MusicDefined = true,
            AmbianceId = row.AmbienteId,
            AmbianceDefined = true,
            Outdoor = row.OutDoor != 0,
            Capabilities = row.Capabilities,
            CapabilitiesDefined = true,
            WorldX = row.X,
            WorldY = row.Y,
            WorldCoordinatesSet = true,
            Cells = cells.ToList(),
        };

        MapCellEditor.SyncDocument(document);
        return true;
    }
}
