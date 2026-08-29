namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.2 — confirmed clip path patterns (relative to clips root).</summary>
public static class VisualClipPaths
{
    public static string ArtworkRelative(int gfxId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"artworks/big/{gfxId}.swf");

    public static string SpriteRelative(int gfxId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"sprites/{gfxId}.swf");

    /// <summary>LIB.1 pattern: clips/items/{floor(gfx/100)}/{gfx}.swf</summary>
    public static string ItemIconRelative(int gfxId)
    {
        var folder = gfxId / 100;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"items/{folder}/{gfxId}.swf");
    }

    /// <summary>Observed on RUFUS clips layout: items/{typeId}/{gfxId}.swf</summary>
    public static string ItemIconRelativeByType(int typeId, int gfxId) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"items/{typeId}/{gfxId}.swf");

    public static (string relative, string? full, bool exists) ResolveItemIcon(
        string? clipsRoot,
        int gfxId,
        int typeId)
    {
        var primaryRel = ItemIconRelative(gfxId);
        var primaryFull = ResolveFull(clipsRoot, primaryRel);
        if (FileExists(primaryFull))
            return (primaryRel, primaryFull, true);

        if (typeId > 0)
        {
            var altRel = ItemIconRelativeByType(typeId, gfxId);
            var altFull = ResolveFull(clipsRoot, altRel);
            if (FileExists(altFull))
                return (altRel, altFull, true);
        }

        return (primaryRel, primaryFull, false);
    }

    public static string? ResolveFull(string? clipsRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(clipsRoot))
            return null;
        return Path.GetFullPath(Path.Combine(clipsRoot.Trim(), relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    public static bool FileExists(string? fullPath) =>
        !string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath);
}
