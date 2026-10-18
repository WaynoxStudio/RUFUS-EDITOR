using System.Globalization;
using System.Text;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>CONTENT.ITEMS.PREVIEW.1 — dev-only batch prep for item icon PNGs under Library/Visuals/Items/.</summary>
public sealed class ItemIconPreviewPrepService
{
    private readonly NpcGfxPreviewPrepService _core = new();

    public static string DefaultArtifactsRoot(string repoRoot) =>
        Path.Combine(repoRoot, "tests", "tools", "artifacts", "item-icon-prep");

    public static string FormatZoomFolder(double zoom) =>
        Math.Abs(zoom - 1.0) < 0.001 ? "native" : $"zoom{zoom.ToString("0.##", CultureInfo.InvariantCulture)}";

    /// <summary>Calibration gfxIDs with diverse export methods (override with --ids).</summary>
    public static IReadOnlyList<(int ItemId, int GfxId, int TypeId)> DefaultCalibrationSamples { get; } =
    [
        (8828, 196, 16),
        (0, 723, 7),
        (0, 10, 1),
        (0, 1, 1),
        (0, 246, 103),
        (0, 441, 107),
        (0, 104, 108),
        (0, 326, 109),
        (0, 40, 110),
        (0, 139, 113),
    ];

    public static bool IsPromotableStatus(NpcGfxPreviewPrepStatus status) =>
        status is NpcGfxPreviewPrepStatus.Ok or NpcGfxPreviewPrepStatus.Review;

    public static IReadOnlyList<ItemIconPreviewSample> BuildUniqueGfxWorklist(
        IReadOnlyList<ItemIconPreviewSample> catalog,
        string clipsRoot)
    {
        var groups = catalog
            .Where(x => x.GfxId > 0)
            .GroupBy(x => x.GfxId)
            .OrderBy(g => g.Key);

        var result = new List<ItemIconPreviewSample>();
        foreach (var g in groups)
        {
            ItemIconPreviewSample? resolved = null;
            foreach (var row in g.OrderBy(x => x.ItemId))
            {
                var (rel, full, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, row.GfxId, row.TypeId);
                if (!exists) continue;
                resolved = new ItemIconPreviewSample
                {
                    ItemId = row.ItemId,
                    GfxId = row.GfxId,
                    TypeId = row.TypeId,
                    Nombre = row.Nombre,
                    Category = row.Category,
                    IconRelativePath = rel,
                    IconFullPath = full,
                };
                break;
            }

            if (resolved is null)
            {
                var row = g.OrderBy(x => x.ItemId).First();
                var (rel, full, _) = VisualClipPaths.ResolveItemIcon(clipsRoot, row.GfxId, row.TypeId);
                resolved = new ItemIconPreviewSample
                {
                    ItemId = row.ItemId,
                    GfxId = row.GfxId,
                    TypeId = row.TypeId,
                    Nombre = row.Nombre,
                    Category = row.Category,
                    IconRelativePath = rel,
                    IconFullPath = full,
                };
            }

            result.Add(resolved);
        }

        return result;
    }

    public static IReadOnlyList<ItemIconPreviewSample> SelectSamplesFromCatalog(
        IReadOnlyList<ItemIconPreviewSample> catalog,
        string clipsRoot,
        int targetCount = 25)
    {
        var worklist = BuildUniqueGfxWorklist(catalog, clipsRoot);
        var picked = new List<ItemIconPreviewSample>();
        var seenTypes = new HashSet<int>();

        foreach (var row in worklist.OrderBy(x => x.TypeId).ThenBy(x => x.ItemId))
        {
            if (!seenTypes.Add(row.TypeId)) continue;
            var (_, _, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, row.GfxId, row.TypeId);
            if (!exists) continue;
            picked.Add(row);
            if (picked.Count >= targetCount) break;
        }

        if (picked.Count < targetCount)
        {
            foreach (var row in worklist.OrderBy(x => x.GfxId))
            {
                if (picked.Any(p => p.GfxId == row.GfxId)) continue;
                var (_, _, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, row.GfxId, row.TypeId);
                if (!exists) continue;
                picked.Add(row);
                if (picked.Count >= targetCount) break;
            }
        }

        return picked;
    }

    public static IReadOnlyList<ItemIconPreviewSample> LoadCatalogFromCache(string cacheJsonPath)
    {
        var payload = VisualLibraryCache.TryReadItems(cacheJsonPath);
        if (payload?.Entries.Count is null or 0)
            return Array.Empty<ItemIconPreviewSample>();

        var list = new List<ItemIconPreviewSample>(payload.Entries.Count);
        foreach (var e in payload.Entries)
        {
            list.Add(new ItemIconPreviewSample
            {
                ItemId = e.ItemId,
                GfxId = e.GfxId,
                TypeId = e.TypeId,
                Nombre = e.Nombre,
                Category = e.Category,
                IconRelativePath = VisualClipPaths.ItemIconRelative(e.GfxId),
            });
        }

        return list;
    }

    public static int CountItemsSharingGfx(IReadOnlyList<ItemIconPreviewSample> catalog, int gfxId) =>
        catalog.Count(x => x.GfxId == gfxId);

    public ItemIconPreviewPrepSummary ProcessSamples(
        ItemIconPreviewPrepOptions options,
        IReadOnlyList<ItemIconPreviewSample> samples,
        IReadOnlyList<ItemIconPreviewSample>? fullCatalog = null)
    {
        var catalog = fullCatalog ?? samples;
        var worklist = samples
            .GroupBy(x => x.GfxId)
            .Select(g => g.First())
            .ToList();
        var skippedDup = Math.Max(0, samples.Count - worklist.Count);

        return ProcessWorklist(
            options,
            catalog,
            worklist,
            samples.Count,
            skippedDup);
    }

    public ItemIconPreviewPrepSummary ProcessFullCatalog(
        ItemIconPreviewPrepOptions options,
        IReadOnlyList<ItemIconPreviewSample> catalog)
    {
        var worklist = BuildUniqueGfxWorklist(catalog, options.ClipsRoot);
        return ProcessWorklist(options, catalog, worklist, catalog.Count, skippedDuplicateGfx: 0);
    }

    private ItemIconPreviewPrepSummary ProcessWorklist(
        ItemIconPreviewPrepOptions options,
        IReadOnlyList<ItemIconPreviewSample> catalog,
        IReadOnlyList<ItemIconPreviewSample> worklist,
        int sampleItemsRequested,
        int skippedDuplicateGfx)
    {
        Directory.CreateDirectory(options.StagingRoot);
        var coreOpts = new NpcGfxPreviewPrepOptions
        {
            FfdecCliPath = options.FfdecCliPath,
            ClipsRoot = options.ClipsRoot,
            StagingRoot = options.StagingRoot,
            LibraryRoot = options.LibraryRoot,
            InkscapeCliPath = options.InkscapeCliPath,
            Zoom = options.Zoom,
            ProcessTimeout = options.ProcessTimeout,
            PromoteOkToLibrary = options.PromoteOkToLibrary,
            VisualCategory = VisualAssetCategory.Items,
            SkipExistingValidStaging = false,
        };

        var entries = new List<ItemIconPreviewPrepEntry>(worklist.Count);
        var skippedLibrary = 0;

        foreach (var sample in worklist)
        {
            var shareCount = CountItemsSharingGfx(catalog, sample.GfxId);
            var libraryPng = NpcGfxPreviewPrepService.ResolveManualPng(
                options.LibraryRoot, sample.GfxId, VisualAssetCategory.Items);

            if (options.SkipExistingLibraryPng
                && libraryPng is not null
                && File.Exists(libraryPng))
            {
                var existing = NpcGfxPngContentValidator.ValidateFile(libraryPng);
                if (existing.Decoded && IsPromotableStatus(existing.ContentStatus))
                {
                    skippedLibrary++;
                    entries.Add(new ItemIconPreviewPrepEntry
                    {
                        GfxId = sample.GfxId,
                        SampleItemId = sample.ItemId,
                        TypeId = sample.TypeId,
                        ItemsSharingGfx = shareCount,
                        SampleNombre = sample.Nombre,
                        IconRelativePath = PortableVisualStore.GetRelativePath(VisualAssetCategory.Items, sample.GfxId),
                        Status = NpcGfxPreviewPrepStatus.ManualExists,
                        Reason = "PNG existente en Library — omitido",
                        OutputPng = libraryPng,
                        SkippedExistingLibrary = true,
                    });
                    continue;
                }
            }

            var (rel, full, exists) = VisualClipPaths.ResolveItemIcon(
                options.ClipsRoot, sample.GfxId, sample.TypeId);
            if (!exists || string.IsNullOrWhiteSpace(full))
            {
                entries.Add(new ItemIconPreviewPrepEntry
                {
                    GfxId = sample.GfxId,
                    SampleItemId = sample.ItemId,
                    TypeId = sample.TypeId,
                    ItemsSharingGfx = shareCount,
                    SampleNombre = sample.Nombre,
                    IconRelativePath = rel,
                    SourceSwf = full,
                    Status = NpcGfxPreviewPrepStatus.NoArtwork,
                    Reason = "NO_ICON — SWF no resuelto en clips",
                });
                continue;
            }

            var core = _core.ProcessSwfFile(
                coreOpts,
                sample.GfxId,
                full,
                $"NO_ICON — sin {rel}");

            entries.Add(new ItemIconPreviewPrepEntry
            {
                GfxId = sample.GfxId,
                SampleItemId = sample.ItemId,
                TypeId = sample.TypeId,
                ItemsSharingGfx = shareCount,
                SampleNombre = sample.Nombre,
                IconRelativePath = rel,
                SourceSwf = core.SourceSwf ?? full,
                Status = core.Status,
                Reason = core.Reason,
                OutputPng = core.OutputPng,
                ExportMethod = core.ExportMethod,
                ExportSpriteId = core.ExportSpriteId,
                Width = core.Width,
                Height = core.Height,
                FileBytes = core.FileBytes,
            });
        }

        var methodCounts = entries
            .GroupBy(e => NormalizeExportMethod(e.ExportMethod))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new ItemIconPreviewPrepSummary
        {
            TotalCatalogItems = catalog.Count,
            SampleItemsRequested = sampleItemsRequested,
            UniqueGfxInCatalog = catalog.Select(x => x.GfxId).Where(x => x > 0).Distinct().Count(),
            UniqueGfxProcessed = entries.Count,
            SkippedDuplicateGfx = skippedDuplicateGfx,
            SkippedExistingLibrary = skippedLibrary,
            Ok = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.Ok),
            Review = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.Review),
            Failed = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.Failed),
            NoIcon = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.NoArtwork),
            ManualExists = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.ManualExists),
            ExportMethodCounts = methodCounts,
            Entries = entries,
            StagingRoot = options.StagingRoot,
        };
    }

    public static ItemIconPromoteResult PromoteFromBatch(
        string batchRoot,
        string libraryRoot,
        string? reportPath = null,
        bool includeReview = true)
    {
        if (string.IsNullOrWhiteSpace(batchRoot) || !Directory.Exists(batchRoot))
            throw new DirectoryNotFoundException("Batch staging no encontrado: " + batchRoot);
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("Library root requerido.", nameof(libraryRoot));

        var manifestPath = Path.Combine(batchRoot, "manifest-native.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("manifest-native.json ausente.", manifestPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!doc.RootElement.TryGetProperty("Entries", out var entriesEl)
            || entriesEl.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Manifest sin Entries[].");

        var created = new List<int>();
        var skippedExisting = new List<int>();
        var skippedStatus = new List<(int GfxId, string Status)>();
        var skippedMissing = new List<int>();
        var promotable = 0;
        var zoomFolder = FormatZoomFolder(1);

        foreach (var e in entriesEl.EnumerateArray())
        {
            if (!e.TryGetProperty("GfxId", out var idEl) || !idEl.TryGetInt32(out var gfxId) || gfxId <= 0)
                continue;

            var status = e.TryGetProperty("Status", out var stEl) && stEl.ValueKind == JsonValueKind.String
                ? stEl.GetString() ?? ""
                : "";

            var isOk = string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase);
            var isReview = string.Equals(status, "REVIEW", StringComparison.OrdinalIgnoreCase);
            if (!isOk && !(includeReview && isReview))
            {
                skippedStatus.Add((gfxId, status));
                continue;
            }

            promotable++;
            var staged = Path.Combine(batchRoot, zoomFolder, gfxId.ToString(CultureInfo.InvariantCulture) + ".png");
            if (!File.Exists(staged)
                && e.TryGetProperty("OutputPng", out var outEl)
                && outEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(outEl.GetString()))
            {
                var hint = outEl.GetString()!;
                staged = Path.IsPathRooted(hint) ? hint : Path.Combine(batchRoot, hint.Replace('/', Path.DirectorySeparatorChar));
            }

            if (!File.Exists(staged))
            {
                skippedMissing.Add(gfxId);
                continue;
            }

            var dest = NpcGfxPreviewPrepService.ResolveManualPng(libraryRoot, gfxId, VisualAssetCategory.Items);
            if (dest is null)
            {
                skippedMissing.Add(gfxId);
                continue;
            }

            if (File.Exists(dest))
            {
                var existing = NpcGfxPngContentValidator.ValidateFile(dest);
                if (existing.Decoded && IsPromotableStatus(existing.ContentStatus))
                {
                    skippedExisting.Add(gfxId);
                    continue;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(staged, dest, overwrite: false);
            if (!File.Exists(dest))
                File.Copy(staged, dest, overwrite: true);
            created.Add(gfxId);
        }

        var result = new ItemIconPromoteResult
        {
            BatchRoot = batchRoot,
            LibraryRoot = libraryRoot,
            PromotableDetected = promotable,
            Created = created,
            SkippedExisting = skippedExisting,
            SkippedStatus = skippedStatus,
            SkippedMissingPng = skippedMissing,
        };

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }

        return result;
    }

    public static string NormalizeExportMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return "none";
        if (method.Contains("full-root-svg", StringComparison.OrdinalIgnoreCase))
            return "full-root-svg";
        if (method.StartsWith("sprite", StringComparison.OrdinalIgnoreCase))
            return "sprite";
        if (method.Contains("frame", StringComparison.OrdinalIgnoreCase))
            return "frame";
        return method.Trim();
    }

    public static void WriteManifest(ItemIconPreviewPrepSummary summary, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dto = new
        {
            summary.TotalCatalogItems,
            summary.SampleItemsRequested,
            summary.UniqueGfxInCatalog,
            summary.UniqueGfxProcessed,
            summary.SkippedDuplicateGfx,
            SkippedExistingLibrary = summary.SkippedExistingLibrary,
            summary.Ok,
            summary.Review,
            summary.Failed,
            NoIcon = summary.NoIcon,
            summary.ManualExists,
            summary.ExportMethodCounts,
            summary.StagingRoot,
            Entries = summary.Entries.Select(e => new
            {
                e.GfxId,
                e.SampleItemId,
                e.TypeId,
                e.ItemsSharingGfx,
                e.SampleNombre,
                e.IconRelativePath,
                e.SourceSwf,
                Status = e.Status.ToString().ToUpperInvariant(),
                e.Reason,
                e.OutputPng,
                e.ExportMethod,
                e.ExportSpriteId,
                e.Width,
                e.Height,
                e.FileBytes,
                e.SkippedDuplicateGfx,
                e.SkippedExistingLibrary,
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public static void WriteSummaryText(ItemIconPreviewPrepSummary summary, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ItemIconPreviewPrep — CONTENT.ITEMS.PREVIEW.1B");
        sb.AppendLine("Staging: " + summary.StagingRoot);
        sb.AppendLine($"Catalog items: {summary.TotalCatalogItems}");
        sb.AppendLine($"Unique GFX: {summary.UniqueGfxInCatalog} · Processed: {summary.UniqueGfxProcessed}");
        sb.AppendLine($"Skipped duplicate GFX rows: {summary.SkippedDuplicateGfx}");
        sb.AppendLine($"Skipped existing Library PNG: {summary.SkippedExistingLibrary}");
        sb.AppendLine($"OK={summary.Ok} REVIEW={summary.Review} FAILED={summary.Failed} NO_ICON={summary.NoIcon} MANUAL={summary.ManualExists}");
        sb.AppendLine("Export methods: " + string.Join(", ", summary.ExportMethodCounts.Select(kv => $"{kv.Key}={kv.Value}")));
        sb.AppendLine();
        foreach (var e in summary.Entries.OrderBy(x => x.GfxId))
        {
            sb.Append("gfx ").Append(e.GfxId.ToString(CultureInfo.InvariantCulture));
            sb.Append(" item ").Append(e.SampleItemId.ToString(CultureInfo.InvariantCulture));
            sb.Append(" share ").Append(e.ItemsSharingGfx.ToString(CultureInfo.InvariantCulture));
            sb.Append(' ').Append(e.Status);
            if (!string.IsNullOrWhiteSpace(e.ExportMethod))
                sb.Append(' ').Append(e.ExportMethod);
            sb.AppendLine();
            sb.Append("  ").AppendLine(e.IconRelativePath);
            if (!string.IsNullOrWhiteSpace(e.Reason))
                sb.Append("  ").AppendLine(e.Reason);
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}
