using System.Globalization;

namespace RufusMapEditor.Rendering.Package;

/// <summary>
/// Paths for Official Map Save under Master/Portable Library.
/// Never hardcodes an absolute install path.
/// </summary>
public static class LibraryMapPaths
{
    public const string MapsFolderName = "Maps";

    public static string GetMapsRoot(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("Library root required.", nameof(libraryRoot));
        return Path.Combine(Path.GetFullPath(libraryRoot), MapsFolderName);
    }

    public static string GetOfficialMapDirectory(string libraryRoot, int mapId)
    {
        if (mapId <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapId), "MapId must be > 0.");
        return Path.Combine(GetMapsRoot(libraryRoot), mapId.ToString(CultureInfo.InvariantCulture));
    }

    public static string GetOfficialRufmapPath(string libraryRoot, int mapId) =>
        Path.Combine(GetOfficialMapDirectory(libraryRoot, mapId), $"{mapId}.rufmap");

    public static string GetOfficialPngPath(string libraryRoot, int mapId) =>
        Path.Combine(GetOfficialMapDirectory(libraryRoot, mapId), $"{mapId}.png");

    public static string GetOfficialAmeSwfPath(string libraryRoot, int mapId) =>
        Path.Combine(GetOfficialMapDirectory(libraryRoot, mapId), $"{mapId}_AME.swf");

    public static string GetOfficialMapDataTxtPath(string libraryRoot, int mapId) =>
        Path.Combine(GetOfficialMapDirectory(libraryRoot, mapId), MapDataPlainText.FileName(mapId));

    /// <summary>True when the folder has a RUFUS official .rufmap (not merely legacy .sql).</summary>
    public static bool HasOfficialSave(string libraryRoot, int mapId) =>
        File.Exists(GetOfficialRufmapPath(libraryRoot, mapId));
}
