using System.Diagnostics;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Gfx;

public sealed class GfxCatalogBuildTimings
{
    public TimeSpan ScanImages { get; init; }
    public TimeSpan ParseXml { get; init; }
    public TimeSpan Total { get; init; }
}

public sealed class GfxCatalogBuildResult
{
    public required GfxCatalog Catalog { get; init; }
    public required IReadOnlyList<GfxCatalogIssue> Issues { get; init; }
    public required GfxCatalogBuildTimings Timings { get; init; }
    public int GroundAnchorEntries { get; init; }
    public int ObjectAnchorEntries { get; init; }
    public int XmlEntriesWithoutImage { get; init; }
    public int ImagesWithoutAnchor { get; init; }
    public int DuplicateImageIds { get; init; }
}

/// <summary>
/// Discovers Astria Images/* and binds grounds/objects anchors from XML without decoding bitmaps.
/// </summary>
public static class AstriaGfxCatalogBuilder
{
    public static GfxCatalogBuildResult Build(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        var totalSw = Stopwatch.StartNew();
        var issues = new List<GfxCatalogIssue>();

        var scanSw = Stopwatch.StartNew();
        var backgrounds = ScanCategory(
            AstriaGfxLibraryLayout.BackgroundsDirectory(installRoot),
            GfxCategory.Background,
            folderIsEmptyForRootFiles: true,
            issues,
            out var bgDups);

        var grounds = ScanCategory(
            AstriaGfxLibraryLayout.GroundsDirectory(installRoot),
            GfxCategory.Ground,
            folderIsEmptyForRootFiles: false,
            issues,
            out var groundDups);

        var objects = ScanCategory(
            AstriaGfxLibraryLayout.ObjectsDirectory(installRoot),
            GfxCategory.Object,
            folderIsEmptyForRootFiles: false,
            issues,
            out var objectDups);
        scanSw.Stop();

        var xmlSw = Stopwatch.StartNew();
        var groundXml = GfxAnchorXmlParser.ParseFile(
            AstriaGfxLibraryLayout.GroundsXmlPath(installRoot),
            GfxCategory.Ground);
        issues.AddRange(groundXml.Issues);

        var objectXml = GfxAnchorXmlParser.ParseFile(
            AstriaGfxLibraryLayout.ObjectsXmlPath(installRoot),
            GfxCategory.Object);
        issues.AddRange(objectXml.Issues);
        xmlSw.Stop();

        var imagesWithoutAnchor = 0;
        var xmlWithoutImage = 0;

        BindAnchors(grounds, groundXml.AnchorsById, groundXml.AmbiguousAnchorsById, GfxCategory.Ground, issues, ref imagesWithoutAnchor, ref xmlWithoutImage);
        BindAnchors(objects, objectXml.AnchorsById, objectXml.AmbiguousAnchorsById, GfxCategory.Object, issues, ref imagesWithoutAnchor, ref xmlWithoutImage);

        // Informational: same numeric ID across categories is legitimate (separate namespaces).
        ReportCrossCategoryOverlaps(backgrounds, grounds, objects, issues);

        totalSw.Stop();

        var catalog = new GfxCatalog(
            backgrounds,
            grounds,
            objects,
            groundXml.AnchorsById.ToDictionary(kv => kv.Key, kv => kv.Value),
            objectXml.AnchorsById.ToDictionary(kv => kv.Key, kv => kv.Value));
        return new GfxCatalogBuildResult
        {
            Catalog = catalog,
            Issues = issues,
            Timings = new GfxCatalogBuildTimings
            {
                ScanImages = scanSw.Elapsed,
                ParseXml = xmlSw.Elapsed,
                Total = totalSw.Elapsed,
            },
            GroundAnchorEntries = groundXml.AnchorsById.Count,
            ObjectAnchorEntries = objectXml.AnchorsById.Count,
            XmlEntriesWithoutImage = xmlWithoutImage,
            ImagesWithoutAnchor = imagesWithoutAnchor,
            DuplicateImageIds = bgDups + groundDups + objectDups,
        };
    }

    /// <summary>
    /// Builds a catalog from an already-materialized folder tree (tests / fixtures).
    /// Expected layout mirrors Astria: Images/backgrounds|grounds|objects and XML/*.xml.
    /// </summary>
    public static GfxCatalogBuildResult BuildFromLayoutRoot(string layoutRoot) => Build(layoutRoot);

    private static Dictionary<int, GfxResource> ScanCategory(
        string directory,
        GfxCategory category,
        bool folderIsEmptyForRootFiles,
        List<GfxCatalogIssue> issues,
        out int duplicateCount)
    {
        duplicateCount = 0;
        var map = new Dictionary<int, GfxResource>();
        if (!Directory.Exists(directory))
        {
            issues.Add(new GfxCatalogIssue
            {
                Severity = GfxIssueSeverity.Error,
                Code = GfxIssueCode.UnreadableFile,
                Category = category,
                Path = directory,
                Message = $"Images directory not found: {directory}",
            });
            return map;
        }

        // Deterministic order; last write wins (Astria array overwrite behaviour).
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        var categoryRoot = Path.GetFullPath(directory);
        foreach (var filePath in files)
        {
            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension))
                continue;

            if (!AstriaGfxLibraryLayout.SupportedExtensions.Contains(extension))
            {
                // Ignore non-image files silently (Astria only loads the supported set).
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            var idText = Path.GetFileNameWithoutExtension(fileName);
            if (!int.TryParse(idText, out var id))
            {
                issues.Add(new GfxCatalogIssue
                {
                    Severity = GfxIssueSeverity.Warning,
                    Code = GfxIssueCode.InvalidFileName,
                    Category = category,
                    Path = filePath,
                    Message = $"Invalid file name (GfxID must be numeric): {fileName}",
                });
                continue;
            }

            string folder;
            var parent = Path.GetDirectoryName(filePath) ?? categoryRoot;
            if (folderIsEmptyForRootFiles ||
                string.Equals(Path.GetFullPath(parent), categoryRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Backgrounds: Astria stores Folder as "".
                // Root-level ground/object files: Astria uses InfosDirectory1.Name which would be "grounds"/"objects";
                // our install has no root-level files under those trees.
                folder = folderIsEmptyForRootFiles ? string.Empty : new DirectoryInfo(parent).Name;
            }
            else
            {
                folder = new DirectoryInfo(parent).Name;
            }

            var resource = new GfxResource
            {
                Id = id,
                Category = category,
                FilePath = Path.GetFullPath(filePath),
                Folder = folder,
                Extension = extension.ToLowerInvariant(),
            };

            var dims = GfxImageDimensions.TryRead(resource.FilePath);
            if (dims is { Width: > 0, Height: > 0 })
            {
                resource = new GfxResource
                {
                    Id = resource.Id,
                    Category = resource.Category,
                    FilePath = resource.FilePath,
                    Folder = resource.Folder,
                    Extension = resource.Extension,
                    Anchor = resource.Anchor,
                    AnchorAmbiguous = resource.AnchorAmbiguous,
                    PixelWidth = dims.Value.Width,
                    PixelHeight = dims.Value.Height,
                };
            }

            if (map.ContainsKey(id))
            {
                duplicateCount++;
                issues.Add(new GfxCatalogIssue
                {
                    Severity = GfxIssueSeverity.Warning,
                    Code = GfxIssueCode.DuplicateGfxId,
                    Category = category,
                    GfxId = id,
                    Path = filePath,
                    Message = $"Duplicate GfxID {id} in {category}; keeping last path '{resource.FilePath}' (previous '{map[id].FilePath}').",
                });
            }

            map[id] = resource;
        }

        return map;
    }

    private static void BindAnchors(
        Dictionary<int, GfxResource> resources,
        IReadOnlyDictionary<int, GfxAnchor> anchors,
        IReadOnlyDictionary<int, IReadOnlyList<GfxAnchor>> ambiguous,
        GfxCategory category,
        List<GfxCatalogIssue> issues,
        ref int imagesWithoutAnchor,
        ref int xmlWithoutImage)
    {
        foreach (var id in resources.Keys.ToList())
        {
            if (anchors.TryGetValue(id, out var anchor))
            {
                var existing = resources[id];
                resources[id] = new GfxResource
                {
                    Id = existing.Id,
                    Category = existing.Category,
                    FilePath = existing.FilePath,
                    Folder = existing.Folder,
                    Extension = existing.Extension,
                    Anchor = anchor,
                    AnchorAmbiguous = ambiguous.ContainsKey(id),
                    PixelWidth = existing.PixelWidth,
                    PixelHeight = existing.PixelHeight,
                };
            }
            else
            {
                imagesWithoutAnchor++;
                issues.Add(new GfxCatalogIssue
                {
                    Severity = GfxIssueSeverity.Warning,
                    Code = GfxIssueCode.ImageWithoutExpectedAnchor,
                    Category = category,
                    GfxId = id,
                    Path = resources[id].FilePath,
                    Message = $"Image GfxID {id} has no anchor entry in {category} XML. Astria would synthesize center-of-image at draw time.",
                });
            }
        }

        foreach (var id in anchors.Keys)
        {
            if (!resources.ContainsKey(id))
            {
                xmlWithoutImage++;
                issues.Add(new GfxCatalogIssue
                {
                    Severity = GfxIssueSeverity.Warning,
                    Code = GfxIssueCode.XmlEntryWithoutImage,
                    Category = category,
                    GfxId = id,
                    Message = $"XML anchor for GfxID {id} has no corresponding {category} image file.",
                });
            }
        }
    }

    private static void ReportCrossCategoryOverlaps(
        Dictionary<int, GfxResource> backgrounds,
        Dictionary<int, GfxResource> grounds,
        Dictionary<int, GfxResource> objects,
        List<GfxCatalogIssue> issues)
    {
        var overlapCount = 0;
        foreach (var id in backgrounds.Keys)
        {
            if (grounds.ContainsKey(id) || objects.ContainsKey(id))
                overlapCount++;
        }

        foreach (var id in grounds.Keys)
        {
            if (objects.ContainsKey(id))
                overlapCount++;
        }

        if (overlapCount == 0)
            return;

        issues.Add(new GfxCatalogIssue
        {
            Severity = GfxIssueSeverity.Info,
            Code = GfxIssueCode.CrossCategoryIdOverlap,
            Message = $"Detected {overlapCount} numeric GfxID overlaps across categories. Namespaces remain separate (Background/Ground/Object).",
        });
    }
}
