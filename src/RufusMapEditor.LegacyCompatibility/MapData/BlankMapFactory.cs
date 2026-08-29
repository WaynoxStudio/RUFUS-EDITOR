using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.MapData;

/// <summary>
/// Creates empty classical maps (Astria-compatible cell count / MapData length).
/// Presets: Medio 15×17, Grande 19×22 (see docs/MULTIMAP_EDITING.md).
/// </summary>
public static class BlankMapFactory
{
    public const int MedioWidth = 15;
    public const int MedioHeight = 17;
    public const int GrandeWidth = 19;
    public const int GrandeHeight = 22;

    public static MapDocument Create(int mapId, int width, int height)
    {
        if (mapId <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapId), "MapId must be > 0.");
        if (width < 1 || width > 100)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1 || height > 100)
            throw new ArgumentOutOfRangeException(nameof(height));

        var count = MapGeometry.CellCount(width, height);
        var cells = new List<CellData>(count);
        for (var i = 0; i < count; i++)
            cells.Add(new CellData());

        var map = new MapDocument
        {
            Id = mapId,
            Width = width,
            Height = height,
            DateMap = "AME",
            Key = string.Empty,
            FightPlaces = string.Empty,
            Outdoor = true,
            Cells = cells,
        };
        MapCellEditor.SyncDocument(map);
        return map;
    }
}
