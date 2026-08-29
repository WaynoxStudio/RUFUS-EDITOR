using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.LegacyCompatibility.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Tests.Gfx;

/// <summary>
/// Integration tests against the real Astria install (read-only).
/// Skipped automatically if the reference path is missing.
/// </summary>
public sealed class AstriaInstallGfxCatalogTests
{
    public const string DefaultAstriaRoot = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1";

    private static string? ResolveAstriaRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ASTRIA_MAP_EDITOR_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;
        if (Directory.Exists(DefaultAstriaRoot))
            return DefaultAstriaRoot;
        return null;
    }

    [Fact]
    public void Real_install_indexes_all_categories_with_o1_lookup()
    {
        var root = ResolveAstriaRoot();
        if (root is null)
            return; // Optional integration environment (set ASTRIA_MAP_EDITOR_ROOT).

        var result = AstriaGfxCatalogBuilder.Build(root);
        var catalog = result.Catalog;
        var summary = GfxCatalogValidator.Summarize(result.Issues);

        Assert.True(catalog.BackgroundCount > 0);
        Assert.True(catalog.GroundCount > 0);
        Assert.True(catalog.ObjectCount > 0);

        var background = catalog.Enumerate(GfxCategory.Background).First();
        var ground = catalog.Enumerate(GfxCategory.Ground).First();
        var obj = catalog.Enumerate(GfxCategory.Object).First();

        Assert.True(catalog.TryGetBackground(background.Id, out var bgResolved));
        Assert.Equal(background.FilePath, bgResolved!.FilePath);

        Assert.True(catalog.TryGetGround(ground.Id, out var groundResolved));
        Assert.Equal(ground.FilePath, groundResolved!.FilePath);

        Assert.True(catalog.TryGetObject(obj.Id, out var objectResolved));
        Assert.Equal(obj.FilePath, objectResolved!.FilePath);

        Assert.False(catalog.TryGetObject(int.MinValue, out var missing));
        Assert.Null(missing);

        Assert.True(ground.HasAnchor, $"Ground {ground.Id} missing anchor");
        Assert.True(obj.HasAnchor, $"Object {obj.Id} missing anchor");

        AssertXmlAnchorMatchesFile(root, GfxCategory.Ground, ground);
        AssertXmlAnchorMatchesFile(root, GfxCategory.Object, obj);

        // Non-fragile performance report for console / debug output.
        Console.WriteLine(
            $"GFX index: BG={catalog.BackgroundCount} GR={catalog.GroundCount} OB={catalog.ObjectCount} Total={catalog.TotalCount}; " +
            $"scan={result.Timings.ScanImages.TotalMilliseconds:F1}ms xml={result.Timings.ParseXml.TotalMilliseconds:F1}ms total={result.Timings.Total.TotalMilliseconds:F1}ms; " +
            $"dupImages={result.DuplicateImageIds} xmlOrphans={result.XmlEntriesWithoutImage} imgMissingAnchor={result.ImagesWithoutAnchor}; " +
            $"issues E/W/I={summary.ErrorCount}/{summary.WarningCount}/{summary.InfoCount}");
    }

    [Fact]
    public void Real_install_preserves_negative_anchors_from_xml()
    {
        var root = ResolveAstriaRoot();
        if (root is null)
            return; // Optional integration environment (set ASTRIA_MAP_EDITOR_ROOT).

        var result = AstriaGfxCatalogBuilder.Build(root);
        var negative = result.Catalog.Enumerate(GfxCategory.Object)
            .Where(r => r.Anchor is { X: < 0 } or { Y: < 0 })
            .Take(1)
            .ToList();

        if (negative.Count == 0)
            return;

        var resource = negative[0];
        AssertXmlAnchorMatchesFile(root, GfxCategory.Object, resource);
        Assert.True(resource.Anchor!.Value.X < 0 || resource.Anchor.Value.Y < 0);
    }

    private static void AssertXmlAnchorMatchesFile(string root, GfxCategory category, GfxResource resource)
    {
        var xmlPath = category switch
        {
            GfxCategory.Ground => AstriaGfxLibraryLayout.GroundsXmlPath(root),
            GfxCategory.Object => AstriaGfxLibraryLayout.ObjectsXmlPath(root),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        var parsed = GfxAnchorXmlParser.ParseFile(xmlPath, category);
        Assert.True(parsed.AnchorsById.TryGetValue(resource.Id, out var expected));
        Assert.Equal(expected, resource.Anchor);
    }
}
