using System.Text;
using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.Rendering.Package;

/// <summary>
/// Writes the canonical MapData string as a plain UTF-8 (no BOM) file with no headers or newlines.
/// Intended for direct copy/paste. Does not re-encode MapData — callers must SyncDocument first.
/// </summary>
public static class MapDataPlainText
{
    public static string FileName(int mapId) => $"{mapId}_MapData.txt";

    /// <summary>
    /// Writes exactly <paramref name="mapData"/> as UTF-8 without BOM, CR, LF, or trailing whitespace.
    /// </summary>
    public static void WriteFile(string path, string mapData)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(mapData);

        // Encoding.UTF8.GetBytes does not emit a BOM.
        var bytes = Encoding.UTF8.GetBytes(mapData);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Reads raw UTF-8 bytes and returns the exact MapData string (no trim).</summary>
    public static string ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Encoding.UTF8.GetString(bytes);
    }

    public static int ExpectedLength(int width, int height) =>
        MapGeometry.ExpectedMapDataLength(width, height);

    public static string CellBlock(string mapData, int cellId)
    {
        ArgumentNullException.ThrowIfNull(mapData);
        var start = cellId * MapDataConstants.CharsPerCell;
        if (start < 0 || start + MapDataConstants.CharsPerCell > mapData.Length)
            throw new ArgumentOutOfRangeException(nameof(cellId));
        return mapData.Substring(start, MapDataConstants.CharsPerCell);
    }
}
