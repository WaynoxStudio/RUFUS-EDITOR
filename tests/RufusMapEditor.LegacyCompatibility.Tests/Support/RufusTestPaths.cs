using RufusMapEditor.LegacyCompatibility.Portable;

namespace RufusMapEditor.LegacyCompatibility.Tests.Support;

public static class RufusTestPaths
{
    public const string AstriaReferenceRoot = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1";

    public static string? ResolveMasterLibrary()
    {
        var repo = RufusLibraryPaths.TryFindRepoMasterLibrary(AppContext.BaseDirectory);
        if (repo is not null && Directory.Exists(Path.Combine(repo, "Maps")))
            return repo;

        var portable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "dist", "RUFUS Map Editor", "Library"));
        if (Directory.Exists(Path.Combine(portable, "Maps")))
            return portable;

        return null;
    }

    public static string? ResolveGfxLibrary() => ResolveMasterLibrary() ?? (
        Directory.Exists(Path.Combine(AstriaReferenceRoot, "Images", "grounds")) ? AstriaReferenceRoot : null);
}
