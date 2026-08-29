using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.Portable;

namespace RufusMapEditor.LegacyCompatibility.Tests.Portable;

public sealed class PortableCatalogParityTests
{
    private const string DefaultAstriaRoot = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1";
    private static readonly string DistLibrary = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "dist", "RUFUS Map Editor", "Library"));

    private static string? ResolveAstriaRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ASTRIA_MAP_EDITOR_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;
        return Directory.Exists(DefaultAstriaRoot) ? DefaultAstriaRoot : null;
    }

    [Fact]
    public void Built_portable_library_matches_reference_catalog_counts()
    {
        var reference = ResolveAstriaRoot();
        if (reference is null || !Directory.Exists(DistLibrary))
            return;

        var refCatalog = AstriaGfxCatalogBuilder.Build(reference).Catalog;
        var portableCatalog = AstriaGfxCatalogBuilder.Build(DistLibrary).Catalog;

        Assert.Equal(refCatalog.TotalCount, portableCatalog.TotalCount);
        Assert.Equal(refCatalog.BackgroundCount, portableCatalog.BackgroundCount);
        Assert.Equal(refCatalog.GroundCount, portableCatalog.GroundCount);
        Assert.Equal(refCatalog.ObjectCount, portableCatalog.ObjectCount);
    }

    [Fact]
    public void Dist_library_passes_portable_validator_when_built()
    {
        if (!Directory.Exists(DistLibrary))
            return;

        var validation = PortableLibraryValidator.Validate(DistLibrary);
        Assert.True(validation.IsValidForEditor);
        Assert.True(validation.HasFlasmExport);
        Assert.Equal(30, validation.MapCount);
    }
}
