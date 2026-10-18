using System.Globalization;
using System.Text;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>CONTENT.ITEMS.PREVIEW.COLLISION.FIX.1A — SWF-keyed item icon prep (parallel pipeline).</summary>
public sealed class ItemIconSwfPreviewPrepService
{
    private readonly NpcGfxPreviewPrepService _core = new();

    private static readonly int[] CollisionSampleGfxIds =
        [1, 2, 6, 7, 8, 10, 11, 19, 24, 268, 292];

    private static readonly string[] MandatorySwfPaths =
    [
        "items/1/10.swf",
        "items/5/10.swf",
        "items/23/10.swf",
    ];

    public static IReadOnlyList<ItemIconSwfWorkItem> BuildUniqueSwfWorklist(
        IReadOnlyList<ItemIconPreviewSample> catalog,
        string clipsRoot)
    {
        var resolvedRows = new List<(ItemIconPreviewSample Row, ItemIconPreviewKey Key, string Rel, string? Full, bool Exists)>();
        foreach (var row in catalog.Where(x => x.GfxId > 0))
        {
            var (rel, full, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, row.GfxId, row.TypeId);
            if (!ItemIconPreviewKey.TryFromResolvedSwfRelativePath(rel, out var key))
                continue;
            resolvedRows.Add((row, key, rel, full, exists));
        }

        return resolvedRows
            .GroupBy(x => x.Key, x => x)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var items = g.OrderBy(x => x.Row.ItemId).ToList();
                var withSwf = items.Where(x => x.Exists).ToList();
                var pick = withSwf.Count > 0 ? withSwf[0] : items[0];
                return new ItemIconSwfWorkItem
                {
                    PreviewKey = pick.Key,
                    GfxId = pick.Row.GfxId,
                    TypeId = pick.Row.TypeId,
                    SampleItemId = pick.Row.ItemId,
                    SampleNombre = pick.Row.Nombre,
                    ResolvedSwfRelativePath = pick.Rel,
                    ResolvedSwfFullPath = pick.Full,
                    SwfExists = pick.Exists,
                    AssociatedItemIds = items.Select(x => x.Row.ItemId).Distinct().OrderBy(x => x).ToList(),
                };
            })
            .ToList();
    }

    public static IReadOnlyList<ItemIconSwfWorkItem> SelectCollisionFixSample(
        IReadOnlyList<ItemIconPreviewSample> catalog,
        string clipsRoot,
        int targetCount = 35)
    {
        var all = BuildUniqueSwfWorklist(catalog, clipsRoot);
        var byPath = all.ToDictionary(x => x.PreviewKey.ResolvedSwfRelativePath, StringComparer.OrdinalIgnoreCase);
        var picked = new List<ItemIconSwfWorkItem>();
        var seen = new HashSet<ItemIconPreviewKey>();

        void TryAdd(ItemIconSwfWorkItem? item)
        {
            if (item is null || !item.SwfExists || !seen.Add(item.PreviewKey))
                return;
            picked.Add(item);
        }

        foreach (var mandatory in MandatorySwfPaths)
        {
            if (byPath.TryGetValue(mandatory, out var item))
                TryAdd(item);
        }

        foreach (var gfxId in CollisionSampleGfxIds)
        {
            foreach (var item in all.Where(x => x.GfxId == gfxId && x.SwfExists).OrderBy(x => x.ResolvedSwfRelativePath))
            {
                TryAdd(item);
                if (picked.Count >= targetCount)
                    return picked;
            }
        }

        foreach (var item in all.Where(x => x.SwfExists).OrderBy(x => x.GfxId).ThenBy(x => x.ResolvedSwfRelativePath))
        {
            TryAdd(item);
            if (picked.Count >= targetCount)
                break;
        }

        return picked;
    }

    public ItemIconSwfPreviewPrepSummary ProcessSwfWorklist(
        ItemIconSwfPreviewPrepOptions options,
        IReadOnlyList<ItemIconPreviewSample> catalog,
        IReadOnlyList<ItemIconSwfWorkItem> worklist,
        int sampleSwfRequested,
        int skippedDuplicateSwf = 0)
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
            PromoteOkToLibrary = false,
            VisualCategory = VisualAssetCategory.Items,
            SkipExistingValidStaging = false,
        };

        var entries = new List<ItemIconSwfPreviewPrepEntry>(worklist.Count);
        var skippedLibrary = 0;

        foreach (var item in worklist)
        {
            var libraryPng = PortableVisualStore.ResolveItemSwfPreviewPngPath(
                options.LibraryRoot, item.PreviewKey.FileStem);

            if (options.SkipExistingLibraryPng
                && libraryPng is not null
                && File.Exists(libraryPng))
            {
                var existing = NpcGfxPngContentValidator.ValidateFile(libraryPng);
                if (existing.Decoded && ItemIconPreviewPrepService.IsPromotableStatus(existing.ContentStatus))
                {
                    skippedLibrary++;
                    entries.Add(new ItemIconSwfPreviewPrepEntry
                    {
                        PreviewKey = item.PreviewKey.Key,
                        ResolvedSwfRelativePath = item.ResolvedSwfRelativePath,
                        GfxId = item.GfxId,
                        TypeId = item.TypeId,
                        SampleItemId = item.SampleItemId,
                        AssociatedItemIds = item.AssociatedItemIds,
                        SampleNombre = item.SampleNombre,
                        Status = NpcGfxPreviewPrepStatus.ManualExists,
                        Reason = "PNG existente en Library/Visuals/ItemsBySwf — omitido",
                        OutputPng = libraryPng,
                        SkippedExistingLibrary = true,
                    });
                    continue;
                }
            }

            if (!item.SwfExists || string.IsNullOrWhiteSpace(item.ResolvedSwfFullPath))
            {
                entries.Add(new ItemIconSwfPreviewPrepEntry
                {
                    PreviewKey = item.PreviewKey.Key,
                    ResolvedSwfRelativePath = item.ResolvedSwfRelativePath,
                    GfxId = item.GfxId,
                    TypeId = item.TypeId,
                    SampleItemId = item.SampleItemId,
                    AssociatedItemIds = item.AssociatedItemIds,
                    SampleNombre = item.SampleNombre,
                    SourceSwf = item.ResolvedSwfFullPath,
                    Status = NpcGfxPreviewPrepStatus.NoArtwork,
                    Reason = "NO_ICON — SWF no resuelto en clips",
                });
                continue;
            }

            var core = _core.ProcessSwfFile(
                coreOpts,
                item.GfxId,
                item.ResolvedSwfFullPath,
                $"NO_ICON — sin {item.ResolvedSwfRelativePath}",
                stagingOutputBaseName: item.PreviewKey.FileStem,
                skipLegacyLibraryLookup: true);

            entries.Add(new ItemIconSwfPreviewPrepEntry
            {
                PreviewKey = item.PreviewKey.Key,
                ResolvedSwfRelativePath = item.ResolvedSwfRelativePath,
                GfxId = item.GfxId,
                TypeId = item.TypeId,
                SampleItemId = item.SampleItemId,
                AssociatedItemIds = item.AssociatedItemIds,
                SampleNombre = item.SampleNombre,
                SourceSwf = core.SourceSwf ?? item.ResolvedSwfFullPath,
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

        var uniqueSwfInCatalog = BuildUniqueSwfWorklist(catalog, options.ClipsRoot).Count;
        var methodCounts = entries
            .GroupBy(e => ItemIconPreviewPrepService.NormalizeExportMethod(e.ExportMethod))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new ItemIconSwfPreviewPrepSummary
        {
            TotalCatalogItems = catalog.Count,
            SampleSwfRequested = sampleSwfRequested,
            UniqueSwfInCatalog = uniqueSwfInCatalog,
            UniqueSwfProcessed = entries.Count,
            SkippedDuplicateSwf = skippedDuplicateSwf,
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

    public ItemIconSwfPreviewPrepSummary ProcessFullCatalog(
        ItemIconSwfPreviewPrepOptions options,
        IReadOnlyList<ItemIconPreviewSample> catalog)
    {
        var worklist = BuildUniqueSwfWorklist(catalog, options.ClipsRoot);
        return ProcessSwfWorklist(options, catalog, worklist, worklist.Count);
    }

    public static ItemIconSwfPromoteResult PromoteSwfFromBatch(
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

        var created = new List<string>();
        var skippedExisting = new List<string>();
        var skippedStatus = new List<(string PreviewKey, string Status)>();
        var skippedMissing = new List<string>();
        var promotable = 0;
        var zoomFolder = ItemIconPreviewPrepService.FormatZoomFolder(1);
        var destDir = Path.Combine(libraryRoot, PortableVisualStore.VisualsFolderName, PortableVisualStore.ItemsBySwfFolderName);
        Directory.CreateDirectory(destDir);

        foreach (var e in entriesEl.EnumerateArray())
        {
            var previewKey = e.TryGetProperty("PreviewKey", out var pkEl) && pkEl.ValueKind == JsonValueKind.String
                ? pkEl.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(previewKey))
                continue;

            if (!ItemIconPreviewKey.TryFromResolvedSwfRelativePath(previewKey, out var key))
                continue;

            var status = e.TryGetProperty("Status", out var stEl) && stEl.ValueKind == JsonValueKind.String
                ? stEl.GetString() ?? ""
                : "";

            var isOk = string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase);
            var isReview = string.Equals(status, "REVIEW", StringComparison.OrdinalIgnoreCase);
            if (!isOk && !(includeReview && isReview))
            {
                skippedStatus.Add((previewKey, status));
                continue;
            }

            promotable++;
            var staged = Path.Combine(batchRoot, zoomFolder, key.StagingPngFileName);
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
                skippedMissing.Add(previewKey);
                continue;
            }

            var dest = Path.Combine(destDir, key.StagingPngFileName);
            if (File.Exists(dest))
            {
                var existing = NpcGfxPngContentValidator.ValidateFile(dest);
                if (existing.Decoded && ItemIconPreviewPrepService.IsPromotableStatus(existing.ContentStatus))
                {
                    skippedExisting.Add(previewKey);
                    continue;
                }
            }

            File.Copy(staged, dest, overwrite: false);
            if (!File.Exists(dest))
                File.Copy(staged, dest, overwrite: true);
            created.Add(previewKey);
        }

        var result = new ItemIconSwfPromoteResult
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

    public static void WriteManifest(ItemIconSwfPreviewPrepSummary summary, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dto = new
        {
            Pipeline = "ItemIconSwfPreviewPrep",
            KeyScheme = "ResolvedSwfRelativePath",
            summary.TotalCatalogItems,
            summary.SampleSwfRequested,
            summary.UniqueSwfInCatalog,
            summary.UniqueSwfProcessed,
            summary.SkippedDuplicateSwf,
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
                PreviewKey = e.PreviewKey,
                e.ResolvedSwfRelativePath,
                e.GfxId,
                e.TypeId,
                e.SampleItemId,
                e.AssociatedItemIds,
                e.SampleNombre,
                e.SourceSwf,
                Status = e.Status.ToString().ToUpperInvariant(),
                e.Reason,
                e.OutputPng,
                e.ExportMethod,
                e.ExportSpriteId,
                e.Width,
                e.Height,
                e.FileBytes,
                e.SkippedExistingLibrary,
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public static void WriteSummaryText(ItemIconSwfPreviewPrepSummary summary, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ItemIconSwfPreviewPrep — CONTENT.ITEMS.PREVIEW.COLLISION.FIX.1A");
        sb.AppendLine("Staging: " + summary.StagingRoot);
        sb.AppendLine($"Catalog items: {summary.TotalCatalogItems}");
        sb.AppendLine($"Unique SWF: {summary.UniqueSwfInCatalog} · Processed: {summary.UniqueSwfProcessed}");
        sb.AppendLine($"Skipped duplicate SWF rows: {summary.SkippedDuplicateSwf}");
        sb.AppendLine($"Skipped existing Library PNG: {summary.SkippedExistingLibrary}");
        sb.AppendLine($"OK={summary.Ok} REVIEW={summary.Review} FAILED={summary.Failed} NO_ICON={summary.NoIcon} MANUAL={summary.ManualExists}");
        sb.AppendLine("Export methods: " + string.Join(", ", summary.ExportMethodCounts.Select(kv => $"{kv.Key}={kv.Value}")));
        sb.AppendLine();
        foreach (var e in summary.Entries.OrderBy(x => x.PreviewKey, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("key ").Append(e.PreviewKey);
            sb.Append(" gfx ").Append(e.GfxId.ToString(CultureInfo.InvariantCulture));
            sb.Append(" item ").Append(e.SampleItemId.ToString(CultureInfo.InvariantCulture));
            sb.Append(" share ").Append(e.AssociatedItemIds.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(' ').Append(e.Status);
            if (!string.IsNullOrWhiteSpace(e.ExportMethod))
                sb.Append(' ').Append(e.ExportMethod);
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(e.Reason))
                sb.Append("  ").AppendLine(e.Reason);
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}
