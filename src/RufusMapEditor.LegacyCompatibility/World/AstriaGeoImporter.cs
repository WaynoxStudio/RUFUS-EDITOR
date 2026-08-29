namespace RufusMapEditor.LegacyCompatibility.World;

/// <summary>
/// Read-only import of Astria .geo island files (BinaryFormatter CellGeo[]).
/// Requires EnableUnsafeBinaryFormatterSerialization at call site.
/// </summary>
public static class AstriaGeoImporter
{
    public sealed class ImportedGeoCell
    {
        public int MapId { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
    }

    public sealed class ImportResult
    {
        public required string IslandName { get; init; }
        public required IReadOnlyList<ImportedGeoCell> Cells { get; init; }
    }

    public static ImportResult Import(string geoFilePath)
    {
        if (!File.Exists(geoFilePath))
            throw new FileNotFoundException(geoFilePath);

        AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", true);
        try
        {
            using var stream = File.OpenRead(geoFilePath);
            var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
#pragma warning disable SYSLIB0011
            var cells = formatter.Deserialize(stream) as Array
                          ?? throw new InvalidDataException("Contenido .geo no reconocido.");
#pragma warning restore SYSLIB0011

            var list = new List<ImportedGeoCell>();
            foreach (var item in cells)
            {
                if (item is null) continue;
                var type = item.GetType();
                var mapId = (int)(type.GetField("MapID")?.GetValue(item) ?? 0);
                var x = (int)(type.GetField("x_pos")?.GetValue(item) ?? 0);
                var y = (int)(type.GetField("y_pos")?.GetValue(item) ?? 0);
                if (mapId > 0)
                    list.Add(new ImportedGeoCell { MapId = mapId, X = x, Y = y });
            }

            var islandName = Path.GetFileNameWithoutExtension(geoFilePath);
            return new ImportResult { IslandName = islandName, Cells = list };
        }
        finally
        {
            AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", false);
        }
    }
}
