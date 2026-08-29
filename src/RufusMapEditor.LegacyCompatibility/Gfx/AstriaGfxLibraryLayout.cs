namespace RufusMapEditor.LegacyCompatibility.Gfx;

/// <summary>
/// Relative layout of an Astria Map Editor installation used for GFX discovery.
/// </summary>
public static class AstriaGfxLibraryLayout
{
    public const string ImagesFolderName = "Images";
    public const string BackgroundsFolderName = "backgrounds";
    public const string GroundsFolderName = "grounds";
    public const string ObjectsFolderName = "objects";
    public const string XmlFolderName = "XML";
    public const string GroundsXmlFileName = "grounds.xml";
    public const string ObjectsXmlFileName = "objects.xml";

    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
    };

    public static string BackgroundsDirectory(string installRoot) =>
        Path.Combine(installRoot, ImagesFolderName, BackgroundsFolderName);

    public static string GroundsDirectory(string installRoot) =>
        Path.Combine(installRoot, ImagesFolderName, GroundsFolderName);

    public static string ObjectsDirectory(string installRoot) =>
        Path.Combine(installRoot, ImagesFolderName, ObjectsFolderName);

    public static string GroundsXmlPath(string installRoot) =>
        Path.Combine(installRoot, XmlFolderName, GroundsXmlFileName);

    public static string ObjectsXmlPath(string installRoot) =>
        Path.Combine(installRoot, XmlFolderName, ObjectsXmlFileName);
}
