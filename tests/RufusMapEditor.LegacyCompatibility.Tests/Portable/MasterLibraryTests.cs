using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.Tests.Support;

namespace RufusMapEditor.LegacyCompatibility.Tests.Portable;

public sealed class MasterLibraryTests
{
    [Fact]
    public void Repo_master_library_exists()
    {
        var root = RufusTestPaths.ResolveMasterLibrary();
        Assert.NotNull(root);
        Assert.True(Directory.Exists(Path.Combine(root!, "Images", "grounds")));
        Assert.True(Directory.Exists(Path.Combine(root, "Images", "objects")));
        Assert.True(File.Exists(Path.Combine(root, "XML", "grounds.xml")));
    }

    [Fact]
    public void Master_library_matches_expected_counts()
    {
        var root = RufusTestPaths.ResolveMasterLibrary();
        if (root is null) return;

        var backgrounds = CountImageFiles(Path.Combine(root, "Images", "backgrounds"));
        var grounds = CountImageFiles(Path.Combine(root, "Images", "grounds"));
        var objects = CountImageFiles(Path.Combine(root, "Images", "objects"));
        var groundIds = CountUniqueIds(Path.Combine(root, "Images", "grounds"));
        var objectIds = CountUniqueIds(Path.Combine(root, "Images", "objects"));
        var maps = Directory.EnumerateDirectories(Path.Combine(root, "Maps"))
            .Count(d =>
            {
                var id = Path.GetFileName(d);
                return File.Exists(Path.Combine(d, $"{id}.sql"))
                    || File.Exists(Path.Combine(d, $"{id}.rufmap"));
            });

        Assert.Equal(48, backgrounds);
        Assert.Equal(549, grounds);
        Assert.Equal(5151, objects);
        Assert.Equal(549, groundIds);
        Assert.Equal(4952, objectIds);
        Assert.Equal(30, maps);
    }

    [Fact]
    public void Sibling_library_resolution_prefers_executable_directory()
    {
        var sibling = PortableLibraryPaths.GetSiblingLibraryPath();
        Assert.EndsWith("Library", sibling, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GfxId_374_resolves_differently_in_master_library()
    {
        var root = RufusTestPaths.ResolveMasterLibrary();
        if (root is null) return;

        var built = AstriaGfxCatalogBuilder.Build(root);
        Assert.True(built.Catalog.TryGet(Domain.Gfx.GfxCategory.Ground, 374, out var g));
        Assert.True(built.Catalog.TryGet(Domain.Gfx.GfxCategory.Object, 374, out var o));
        Assert.NotEqual(g!.FilePath, o!.FilePath, StringComparer.OrdinalIgnoreCase);
    }

    private static int CountImageFiles(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Count(f => AstriaGfxLibraryLayout.SupportedExtensions.Contains(Path.GetExtension(f)))
            : 0;

    private static int CountUniqueIds(string dir)
    {
        var ids = new HashSet<int>();
        if (!Directory.Exists(dir)) return 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
        {
            if (!AstriaGfxLibraryLayout.SupportedExtensions.Contains(Path.GetExtension(file))) continue;
            if (int.TryParse(Path.GetFileNameWithoutExtension(file), out var id))
                ids.Add(id);
        }
        return ids.Count;
    }
}
