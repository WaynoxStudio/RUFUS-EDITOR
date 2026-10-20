using System.Globalization;
using System.Text;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>CONTENT.ITEMS.PREVIEW.COLLISION.AUDIT.1 — dev-only collision analysis.</summary>
public static class ItemIconCollisionAudit
{
    public sealed record ItemRow
    {
        public required int ItemId { get; init; }
        public required int TypeId { get; init; }
        public required int GfxId { get; init; }
        public required string Nombre { get; init; }
        public required string ResolvedSwfRelativePath { get; init; }
        public string? ResolvedSwfAbsolutePath { get; init; }
        public required bool SwfExists { get; init; }
        public required string CurrentPreviewKey { get; init; }
        public string CollisionGroup { get; init; } = "";
        public bool MatchesBatchSwf { get; init; }
        public bool PotentiallyWrongPreview { get; init; }
    }

    public sealed class GfxGroupSummary
    {
        public required int GfxId { get; init; }
        public required string Classification { get; init; }
        public int ItemCount { get; init; }
        public int DistinctTypeIds { get; init; }
        public int DistinctResolvedSwfPaths { get; init; }
        public string? BatchResolvedSwf { get; init; }
        public string? BatchSampleItemId { get; init; }
        public int ItemsMatchingBatchSwf { get; init; }
        public int ItemsPotentiallyWrong { get; init; }
        public required IReadOnlyList<(string SwfPath, int TypeId, int ItemCount)> SwfBreakdown { get; init; }
    }

    public sealed class AuditResult
    {
        public required int TotalItems { get; init; }
        public required int UniqueGfx { get; init; }
        public required int GfxSafe { get; init; }
        public required int GfxCollision { get; init; }
        public required int GfxNoIcon { get; init; }
        public required int ItemsInSafeGfx { get; init; }
        public required int ItemsInCollisionGfx { get; init; }
        public required int ItemsPotentiallyWrongPreview { get; init; }
        public required int ItemsNoIcon { get; init; }
        public required IReadOnlyList<GfxGroupSummary> CollisionGroups { get; init; }
        public required IReadOnlyList<ItemRow> AllRows { get; init; }
    }

    public static AuditResult Run(
        IReadOnlyList<ItemIconPreviewSample> catalog,
        string clipsRoot,
        string? batchManifestPath = null)
    {
        var batchByGfx = LoadBatchSwfByGfx(batchManifestPath, catalog, clipsRoot);
        var rows = new List<ItemRow>(catalog.Count);

        foreach (var item in catalog)
        {
            var (rel, full, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, item.GfxId, item.TypeId);
            var previewKey = item.GfxId > 0
                ? PortableVisualStore.GetRelativePath(VisualAssetCategory.Items, item.GfxId)
                : "(none)";

            rows.Add(new ItemRow
            {
                ItemId = item.ItemId,
                TypeId = item.TypeId,
                GfxId = item.GfxId,
                Nombre = item.Nombre,
                ResolvedSwfRelativePath = rel,
                ResolvedSwfAbsolutePath = full,
                SwfExists = exists,
                CurrentPreviewKey = previewKey,
                CollisionGroup = "", // filled below
                MatchesBatchSwf = false,
                PotentiallyWrongPreview = false,
            });
        }

        var groups = rows
            .Where(r => r.GfxId > 0)
            .GroupBy(r => r.GfxId)
            .OrderBy(g => g.Key)
            .ToList();

        var summaries = new List<GfxGroupSummary>();
        var collisionSummaries = new List<GfxGroupSummary>();

        foreach (var g in groups)
        {
            var items = g.ToList();
            var existing = items.Where(i => i.SwfExists).ToList();
            var distinctSwfs = existing
                .Select(i => i.ResolvedSwfRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string classification;
            if (existing.Count == 0)
                classification = "NO_ICON";
            else if (distinctSwfs.Count <= 1)
                classification = "SAFE";
            else
                classification = "COLLISION";

            string? batchSwf = null;
            int batchSampleItemId = 0;
            if (batchByGfx.TryGetValue(g.Key, out var batchInfo))
            {
                batchSwf = batchInfo.SwfRelative;
                batchSampleItemId = batchInfo.SampleItemId;
            }

            var swfBreakdown = existing
                .GroupBy(i => i.ResolvedSwfRelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(sg => (SwfPath: sg.Key, TypeId: sg.First().TypeId, ItemCount: sg.Count()))
                .OrderBy(x => x.SwfPath)
                .ToList();

            var matchingBatch = string.IsNullOrWhiteSpace(batchSwf)
                ? 0
                : existing.Count(i => string.Equals(i.ResolvedSwfRelativePath, batchSwf, StringComparison.OrdinalIgnoreCase));

            var potentiallyWrong = classification == "COLLISION" && !string.IsNullOrWhiteSpace(batchSwf)
                ? existing.Count(i => !string.Equals(i.ResolvedSwfRelativePath, batchSwf, StringComparison.OrdinalIgnoreCase))
                : 0;

            foreach (var item in items)
            {
                var idx = rows.FindIndex(r => r.ItemId == item.ItemId);
                if (idx < 0) continue;
                var matchesBatch = !string.IsNullOrWhiteSpace(batchSwf)
                                   && item.SwfExists
                                   && string.Equals(item.ResolvedSwfRelativePath, batchSwf, StringComparison.OrdinalIgnoreCase);
                var wrong = classification == "COLLISION"
                            && item.SwfExists
                            && !string.IsNullOrWhiteSpace(batchSwf)
                            && !matchesBatch;
                rows[idx] = rows[idx] with
                {
                    CollisionGroup = classification,
                    MatchesBatchSwf = matchesBatch,
                    PotentiallyWrongPreview = wrong,
                };
            }

            var summary = new GfxGroupSummary
            {
                GfxId = g.Key,
                Classification = classification,
                ItemCount = items.Count,
                DistinctTypeIds = items.Select(i => i.TypeId).Distinct().Count(),
                DistinctResolvedSwfPaths = distinctSwfs.Count,
                BatchResolvedSwf = batchSwf,
                BatchSampleItemId = batchSampleItemId > 0
                    ? batchSampleItemId.ToString(CultureInfo.InvariantCulture)
                    : null,
                ItemsMatchingBatchSwf = matchingBatch,
                ItemsPotentiallyWrong = potentiallyWrong,
                SwfBreakdown = swfBreakdown,
            };
            summaries.Add(summary);
            if (classification == "COLLISION")
                collisionSummaries.Add(summary);
        }

        var gfxZero = rows.Where(r => r.GfxId <= 0).ToList();
        foreach (var z in gfxZero)
        {
            var idx = rows.FindIndex(r => r.ItemId == z.ItemId);
            if (idx >= 0)
                rows[idx] = rows[idx] with { CollisionGroup = "NO_ICON" };
        }

        return new AuditResult
        {
            TotalItems = catalog.Count,
            UniqueGfx = groups.Count,
            GfxSafe = summaries.Count(s => s.Classification == "SAFE"),
            GfxCollision = summaries.Count(s => s.Classification == "COLLISION"),
            GfxNoIcon = summaries.Count(s => s.Classification == "NO_ICON"),
            ItemsInSafeGfx = summaries.Where(s => s.Classification == "SAFE").Sum(s => s.ItemCount),
            ItemsInCollisionGfx = summaries.Where(s => s.Classification == "COLLISION").Sum(s => s.ItemCount),
            ItemsPotentiallyWrongPreview = rows.Count(r => r.PotentiallyWrongPreview),
            ItemsNoIcon = rows.Count(r => !r.SwfExists && r.GfxId > 0) + gfxZero.Count,
            CollisionGroups = collisionSummaries,
            AllRows = rows,
        };
    }

    public static void WriteReports(AuditResult result, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var summary = new
        {
            result.TotalItems,
            result.UniqueGfx,
            GfxSafe = result.GfxSafe,
            GfxCollision = result.GfxCollision,
            GfxNoIcon = result.GfxNoIcon,
            result.ItemsInSafeGfx,
            result.ItemsInCollisionGfx,
            result.ItemsPotentiallyWrongPreview,
            result.ItemsNoIcon,
            CollisionExamples = result.CollisionGroups.Take(30).Select(g => new
            {
                g.GfxId,
                g.ItemCount,
                g.BatchResolvedSwf,
                g.BatchSampleItemId,
                g.ItemsPotentiallyWrong,
                Paths = g.SwfBreakdown.Select(p => new
                {
                    p.SwfPath,
                    p.TypeId,
                    p.ItemCount,
                }),
            }),
        };
        File.WriteAllText(
            Path.Combine(outputDirectory, "collision-summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(outputDirectory, "collision-items.json"),
            JsonSerializer.Serialize(result.AllRows.Select(r => new
            {
                r.ItemId,
                r.TypeId,
                r.GfxId,
                r.Nombre,
                r.ResolvedSwfRelativePath,
                r.ResolvedSwfAbsolutePath,
                r.SwfExists,
                r.CurrentPreviewKey,
                r.CollisionGroup,
                r.MatchesBatchSwf,
                r.PotentiallyWrongPreview,
            }), new JsonSerializerOptions { WriteIndented = false }),
            Encoding.UTF8);

        var sb = new StringBuilder();
        sb.AppendLine("ItemIconCollisionAudit — CONTENT.ITEMS.PREVIEW.COLLISION.AUDIT.1");
        sb.AppendLine($"Items: {result.TotalItems} · GFX únicos: {result.UniqueGfx}");
        sb.AppendLine($"SAFE={result.GfxSafe} COLLISION={result.GfxCollision} NO_ICON={result.GfxNoIcon}");
        sb.AppendLine($"Items en SAFE: {result.ItemsInSafeGfx}");
        sb.AppendLine($"Items en COLLISION groups: {result.ItemsInCollisionGfx}");
        sb.AppendLine($"Items potencialmente preview incorrecto: {result.ItemsPotentiallyWrongPreview}");
        sb.AppendLine();
        sb.AppendLine("=== Primeras 30 colisiones ===");
        foreach (var g in result.CollisionGroups.Take(30))
        {
            sb.Append("GFX ").Append(g.GfxId.ToString(CultureInfo.InvariantCulture));
            sb.Append(" · batch SWF=").Append(g.BatchResolvedSwf ?? "(none)");
            sb.Append(" · sample item=").AppendLine(g.BatchSampleItemId ?? "?");
            foreach (var p in g.SwfBreakdown)
                sb.Append("  type ").Append(p.TypeId).Append(" → ").Append(p.SwfPath)
                    .Append(" → ").Append(p.ItemCount).AppendLine(" items");
        }

        File.WriteAllText(Path.Combine(outputDirectory, "collision-summary.txt"), sb.ToString(), Encoding.UTF8);

        var gfx10 = result.AllRows.Where(r => r.GfxId == 10).OrderBy(r => r.ItemId).ToList();
        if (gfx10.Count > 0)
        {
            var g10 = new StringBuilder();
            g10.AppendLine("GFX 10 — detalle");
            foreach (var r in gfx10)
            {
                g10.Append(r.ItemId).Append('\t').Append(r.Nombre).Append('\t')
                    .Append("type=").Append(r.TypeId).Append('\t')
                    .Append(r.ResolvedSwfRelativePath).Append('\t')
                    .Append(r.SwfExists ? "exists" : "missing").Append('\t')
                    .Append(r.PotentiallyWrongPreview ? "WRONG" : "ok")
                    .AppendLine();
            }
            File.WriteAllText(Path.Combine(outputDirectory, "gfx-10-detail.txt"), g10.ToString(), Encoding.UTF8);
        }

        var withSwf = result.AllRows.Where(r => r.SwfExists && r.GfxId > 0).ToList();
        var byTypeGfx = withSwf.GroupBy(r => (r.TypeId, r.GfxId)).Count();
        var bySwf = withSwf.GroupBy(r => r.ResolvedSwfRelativePath, StringComparer.OrdinalIgnoreCase).Count();
        var keyCompare = new
        {
            UniqueKeys_TypeIdPlusGfxId = byTypeGfx,
            UniqueKeys_ResolvedSwfPath = bySwf,
            OverDedupByTypeGfx = byTypeGfx - bySwf,
            SafePreviewGfxIds = result.AllRows.Where(r => r.CollisionGroup == "SAFE").Select(r => r.GfxId).Distinct().OrderBy(x => x).ToList(),
            CollidedPreviewGfxIds = result.CollisionGroups.Select(g => g.GfxId).OrderBy(x => x).ToList(),
            PotentiallyWrongItemIds = result.AllRows.Where(r => r.PotentiallyWrongPreview).Select(r => r.ItemId).OrderBy(x => x).ToList(),
        };
        File.WriteAllText(
            Path.Combine(outputDirectory, "key-comparison.json"),
            JsonSerializer.Serialize(keyCompare, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    private static Dictionary<int, (string? SwfRelative, int SampleItemId)> LoadBatchSwfByGfx(
        string? manifestPath,
        IReadOnlyList<ItemIconPreviewSample> catalog,
        string clipsRoot)
    {
        var map = new Dictionary<int, (string? SwfRelative, int SampleItemId)>();
        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.TryGetProperty("Entries", out var entries))
            {
                foreach (var e in entries.EnumerateArray())
                {
                    if (!e.TryGetProperty("GfxId", out var gEl) || !gEl.TryGetInt32(out var gfxId))
                        continue;
                    var sampleItemId = e.TryGetProperty("SampleItemId", out var si) && si.TryGetInt32(out var sid) ? sid : 0;
                    string? swf = null;
                    if (e.TryGetProperty("SourceSwf", out var ss) && ss.ValueKind == JsonValueKind.String)
                        swf = ExtractItemsRelative(ss.GetString());
                    if (swf is null
                        && e.TryGetProperty("IconRelativePath", out var ip)
                        && ip.ValueKind == JsonValueKind.String)
                    {
                        var path = ip.GetString() ?? "";
                        if (path.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                            swf = path.Replace('\\', '/');
                    }

                    if (swf is null && sampleItemId > 0
                        && e.TryGetProperty("TypeId", out var ti) && ti.TryGetInt32(out var typeId))
                    {
                        var (rel, _, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, gfxId, typeId);
                        if (exists)
                            swf = rel;
                    }

                    map[gfxId] = (swf, sampleItemId);
                }
            }
        }

        foreach (var winner in ItemIconPreviewPrepService.BuildUniqueGfxWorklist(catalog, clipsRoot))
        {
            if (!map.TryGetValue(winner.GfxId, out var prev) || string.IsNullOrWhiteSpace(prev.SwfRelative))
            {
                map[winner.GfxId] = (winner.IconRelativePath, winner.ItemId);
            }
        }

        return map;
    }

    private static string? ExtractItemsRelative(string? absoluteOrRelative)
    {
        if (string.IsNullOrWhiteSpace(absoluteOrRelative))
            return null;
        var normalized = absoluteOrRelative.Replace('\\', '/');
        var idx = normalized.IndexOf("items/", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? normalized[idx..] : null;
    }
}
