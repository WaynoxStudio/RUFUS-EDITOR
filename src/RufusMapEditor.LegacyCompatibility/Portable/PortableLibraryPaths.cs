namespace RufusMapEditor.LegacyCompatibility.Portable;

/// <summary>
/// Resolves portable <c>.\Library\</c> relative to the host executable (not cwd).
/// </summary>
public static class PortableLibraryPaths
{
    public const string LibraryFolderName = "Library";

    /// <summary>Directory containing the running executable.</summary>
    public static string GetApplicationDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var dir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(dir))
                return dir;
        }

        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string GetSiblingLibraryPath(string? applicationDirectory = null) =>
        Path.Combine(applicationDirectory ?? GetApplicationDirectory(), LibraryFolderName);

    public static bool TryResolveSiblingLibrary(out string libraryPath)
    {
        libraryPath = GetSiblingLibraryPath();
        return Directory.Exists(libraryPath);
    }
}
