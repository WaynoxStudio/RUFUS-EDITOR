using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.World;

public static class MapDocumentDuplicator
{
    public static MapDocument DeepCopy(MapDocument source, int newMapId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (newMapId <= 0)
            throw new ArgumentOutOfRangeException(nameof(newMapId));

        var copy = new MapDocument
        {
            Id = newMapId,
            Width = source.Width,
            Height = source.Height,
            DateMap = source.DateMap ?? "AME",
            Key = source.Key ?? string.Empty,
            FightPlaces = source.FightPlaces ?? string.Empty,
            BackgroundId = source.BackgroundId, BackgroundDefined = source.BackgroundDefined,
            MusicId = source.MusicId, MusicDefined = source.MusicDefined,
            AmbianceId = source.AmbianceId, AmbianceDefined = source.AmbianceDefined,
            Capabilities = source.Capabilities, CapabilitiesDefined = source.CapabilitiesDefined,
            Outdoor = source.Outdoor,
            WorldX = source.WorldX,
            WorldY = source.WorldY,
            WorldCoordinatesSet = source.WorldCoordinatesSet,
            Cells = source.Cells.Select(MapCellEditor.Clone).ToList(),
        };
        MapCellEditor.SyncMapDataString(copy);
        return copy;
    }

    public static bool ContentEquals(MapDocument a, MapDocument b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        if (a.BackgroundId != b.BackgroundId) return false;
        if (a.MusicId != b.MusicId) return false;
        if (a.AmbianceId != b.AmbianceId) return false;
        if (a.Capabilities != b.Capabilities) return false;
        if (a.Outdoor != b.Outdoor) return false;
        if (a.Cells.Count != b.Cells.Count) return false;
        for (var i = 0; i < a.Cells.Count; i++)
        {
            if (!MapCellEditor.CellEquals(a.Cells[i], b.Cells[i]))
                return false;
        }

        return a.MapData == b.MapData;
    }
}
