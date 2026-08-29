namespace RufusMapEditor.LegacyCompatibility.Portable;

/// <summary>
/// Resolves the RUFUS Master Library folder in the repository or next to the executable.
/// </summary>
public static class RufusLibraryPaths
{
    public const string MasterLibraryFolderName = "Library";

    /// <summary>
    /// Walks upward from <paramref name="startDirectory"/> looking for <c>{root}/Library/Maps</c>.
    /// </summary>
    public static string? TryFindRepoMasterLibrary(string? startDirectory = null)
    {
        var dir = startDirectory ?? AppContext.BaseDirectory;
        for (var i = 0; i < 12 && !string.IsNullOrWhiteSpace(dir); i++)
        {
            var candidate = Path.Combine(dir, MasterLibraryFolderName);
            if (Directory.Exists(Path.Combine(candidate, "Maps"))
                && Directory.Exists(Path.Combine(candidate, "Images", "grounds")))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    public static string? TryResolveEffectiveLibrary(out LibrarySource source)
    {
        if (PortableLibraryPaths.TryResolveSiblingLibrary(out var sibling)
            && PortableLibraryValidator.Validate(sibling).IsValidForEditor)
        {
            source = LibrarySource.SiblingExecutable;
            return sibling;
        }

        var repo = TryFindRepoMasterLibrary();
        if (repo is not null && PortableLibraryValidator.Validate(repo).IsValidForEditor)
        {
            source = LibrarySource.RepoMaster;
            return repo;
        }

        source = LibrarySource.None;
        return null;
    }
}

public enum LibrarySource
{
    None = 0,
    SiblingExecutable = 1,
    RepoMaster = 2,
    UserSettings = 3,
    ManualSelection = 4,
}
