using System.Globalization;
using System.Text;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

// CONTENT.ITEMS.PREVIEW.1B — DEV-ONLY FFDec prep + promote for item icon PNGs.
Console.OutputEncoding = Encoding.UTF8;

var argsMap = ParseArgs(args);
if (argsMap.ContainsKey("help") || argsMap.ContainsKey("h"))
{
    PrintHelp();
    return 0;
}

var repoRoot = FindRepoRoot();
var settings = TryLoadSettings();
var clips = argsMap.GetValueOrDefault("clips")
            ?? settings?.ClipsRootPath
            ?? ClipsRootPaths.TryDiscoverUnambiguous();
var clipsValidation = ClipsRootPaths.Validate(clips);
if (!clipsValidation.IsValid || clipsValidation.NormalizedPath is null)
{
    Console.Error.WriteLine("ERROR: clips root inválido.");
    Console.Error.WriteLine(clipsValidation.Message);
    return 2;
}

clips = clipsValidation.NormalizedPath;

if (argsMap.ContainsKey("audit-collision"))
{
    var auditLibrary = argsMap.GetValueOrDefault("library")
                     ?? Path.Combine(repoRoot, "Library");
    var auditCachePath = argsMap.GetValueOrDefault("items-cache") ?? FindLatestItemsCache();
    if (auditCachePath is null || !File.Exists(auditCachePath))
    {
        Console.Error.WriteLine("ERROR: caché items_es vacía.");
        return 4;
    }

    var auditCatalog = ItemIconPreviewPrepService.LoadCatalogFromCache(auditCachePath);
    var batchManifest = argsMap.GetValueOrDefault("batch-manifest");
    if (string.IsNullOrWhiteSpace(batchManifest))
    {
        var defaultBatch = Path.Combine(
            ItemIconPreviewPrepService.DefaultArtifactsRoot(repoRoot),
            "batch-20260902-180542",
            "manifest-native.json");
        if (File.Exists(defaultBatch))
            batchManifest = defaultBatch;
    }

    Console.WriteLine("ItemIconCollisionAudit · CONTENT.ITEMS.PREVIEW.COLLISION.AUDIT.1");
    Console.WriteLine("Clips:   " + clips);
    Console.WriteLine("Catalog: " + auditCachePath + " · " + auditCatalog.Count);
    Console.WriteLine("Batch:   " + (batchManifest ?? "(none)"));
    Console.WriteLine("Library: " + auditLibrary);

    var auditResult = ItemIconCollisionAudit.Run(auditCatalog, clips, batchManifest);
    var auditRunId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    var outDir = Path.Combine(
        ItemIconPreviewPrepService.DefaultArtifactsRoot(repoRoot),
        "collision-audit-" + auditRunId);
    ItemIconCollisionAudit.WriteReports(auditResult, outDir);

    Console.WriteLine();
    Console.WriteLine($"Items totales:              {auditResult.TotalItems}");
    Console.WriteLine($"GFX únicos:                 {auditResult.UniqueGfx}");
    Console.WriteLine($"GFX SAFE:                   {auditResult.GfxSafe}");
    Console.WriteLine($"GFX COLLISION:              {auditResult.GfxCollision}");
    Console.WriteLine($"GFX NO_ICON:                {auditResult.GfxNoIcon}");
    Console.WriteLine($"Items en grupos colisión:   {auditResult.ItemsInCollisionGfx}");
    Console.WriteLine($"Items preview incorrecto:   {auditResult.ItemsPotentiallyWrongPreview}");
    Console.WriteLine("Report: " + outDir);
    return 0;
}

var ffdec = argsMap.GetValueOrDefault("ffdec")
            ?? Environment.GetEnvironmentVariable("RUFUS_FFDEC_CLI")
            ?? Path.Combine(repoRoot, "tools", "_ffdec", "extracted", "ffdec-cli.exe");

var library = argsMap.GetValueOrDefault("library")
              ?? Path.Combine(repoRoot, "Library");

if (argsMap.TryGetValue("promote-from", out var promoteFrom) && !string.IsNullOrWhiteSpace(promoteFrom))
{
    var batchRoot = Path.GetFullPath(promoteFrom);
    var reportPath = Path.Combine(batchRoot, "promote-report.json");
    Console.WriteLine("PrepareItemIconPreviews · PROMOTE-FROM");
    Console.WriteLine("Batch:   " + batchRoot);
    Console.WriteLine("Library: " + library);
    var result = ItemIconPreviewPrepService.PromoteFromBatch(batchRoot, library, reportPath, includeReview: true);
    Console.WriteLine($"Promotable: {result.PromotableDetected}");
    Console.WriteLine($"CREATED:    {result.Created.Count}");
    Console.WriteLine($"SKIPPED_EXISTING: {result.SkippedExisting.Count}");
    Console.WriteLine($"SKIPPED_STATUS:   {result.SkippedStatus.Count}");
    Console.WriteLine($"SKIPPED_MISSING:  {result.SkippedMissingPng.Count}");
    Console.WriteLine("Report: " + reportPath);
    return 0;
}

if (argsMap.TryGetValue("promote-swf-from", out var promoteSwfFrom) && !string.IsNullOrWhiteSpace(promoteSwfFrom))
{
    var batchRoot = Path.GetFullPath(promoteSwfFrom);
    var reportPath = Path.Combine(batchRoot, "promote-swf-report.json");
    Console.WriteLine("PrepareItemIconPreviews · PROMOTE-SWF-FROM (ItemsBySwf)");
    Console.WriteLine("Batch:   " + batchRoot);
    Console.WriteLine("Library: " + library);
    var result = ItemIconSwfPreviewPrepService.PromoteSwfFromBatch(batchRoot, library, reportPath, includeReview: true);
    Console.WriteLine($"Promotable: {result.PromotableDetected}");
    Console.WriteLine($"CREATED:    {result.Created.Count}");
    Console.WriteLine($"SKIPPED_EXISTING: {result.SkippedExisting.Count}");
    Console.WriteLine($"SKIPPED_STATUS:   {result.SkippedStatus.Count}");
    Console.WriteLine($"SKIPPED_MISSING:  {result.SkippedMissingPng.Count}");
    Console.WriteLine("Report: " + reportPath);
    return 0;
}

if (string.IsNullOrWhiteSpace(ffdec) || !File.Exists(ffdec))
{
    Console.Error.WriteLine("ERROR: ffdec-cli.exe no encontrado.");
    return 3;
}

var inkscape = argsMap.GetValueOrDefault("inkscape")
               ?? Environment.GetEnvironmentVariable("RUFUS_INKSCAPE_CLI")
               ?? InkscapeCliProcessRunner.TryDiscoverInkscapeCli()
               ?? "";
if (!string.IsNullOrWhiteSpace(inkscape) && !File.Exists(inkscape))
    inkscape = "";

if (argsMap.ContainsKey("collision-fix-sample"))
{
    var cachePathSwf = argsMap.GetValueOrDefault("items-cache") ?? FindLatestItemsCache();
    if (cachePathSwf is null || !File.Exists(cachePathSwf))
    {
        Console.Error.WriteLine("ERROR: caché items_es vacía.");
        return 4;
    }

    var catalogSwf = ItemIconPreviewPrepService.LoadCatalogFromCache(cachePathSwf);
    var sampleSwfCount = int.TryParse(argsMap.GetValueOrDefault("count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sc)
        ? Math.Clamp(sc, 10, 60)
        : 35;
    var timeoutSwf = int.TryParse(argsMap.GetValueOrDefault("timeout"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts)
        ? Math.Clamp(ts, 5, 300)
        : 45;

    var worklist = ItemIconSwfPreviewPrepService.SelectCollisionFixSample(catalogSwf, clips, sampleSwfCount);
    if (worklist.Count == 0)
    {
        Console.Error.WriteLine("ERROR: muestra SWF vacía.");
        return 5;
    }

    var runIdSwf = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    var stagingSwf = Path.Combine(
        ItemIconPreviewPrepService.DefaultArtifactsRoot(repoRoot),
        "collision-fix-sample-" + runIdSwf);
    Directory.CreateDirectory(stagingSwf);

    Console.WriteLine("ItemIconSwfPreviewPrep · CONTENT.ITEMS.PREVIEW.COLLISION.FIX.1A");
    Console.WriteLine("Clips:    " + clips);
    Console.WriteLine("FFDec:    " + ffdec);
    Console.WriteLine("Inkscape: " + (string.IsNullOrWhiteSpace(inkscape) ? "(no)" : inkscape));
    Console.WriteLine("Library:  " + library + " (legacy Items/ intacto)");
    Console.WriteLine($"Catálogo: {cachePathSwf} · SWF sample: {worklist.Count}");
    Console.WriteLine();

    var swfSvc = new ItemIconSwfPreviewPrepService();
    var swfOpts = new ItemIconSwfPreviewPrepOptions
    {
        FfdecCliPath = ffdec,
        ClipsRoot = clips,
        StagingRoot = stagingSwf,
        LibraryRoot = library,
        InkscapeCliPath = string.IsNullOrWhiteSpace(inkscape) ? null : inkscape,
        ProcessTimeout = TimeSpan.FromSeconds(timeoutSwf),
        PromoteOkToLibrary = false,
        SkipExistingLibraryPng = false,
    };

    var swfSummary = swfSvc.ProcessSwfWorklist(swfOpts, catalogSwf, worklist, worklist.Count);
    ItemIconSwfPreviewPrepService.WriteManifest(swfSummary, Path.Combine(stagingSwf, "manifest-native.json"));
    ItemIconSwfPreviewPrepService.WriteSummaryText(swfSummary, Path.Combine(stagingSwf, "summary.txt"));

    var gfx10 = swfSummary.Entries.Where(e => e.GfxId == 10).OrderBy(e => e.PreviewKey).ToList();
    var gfx10Report = new StringBuilder();
    gfx10Report.AppendLine("GFX 10 validation");
    foreach (var e in gfx10)
    {
        gfx10Report.Append(e.PreviewKey).Append(" → ").Append(e.Status);
        if (!string.IsNullOrWhiteSpace(e.OutputPng))
            gfx10Report.Append(" → ").Append(e.OutputPng);
        gfx10Report.AppendLine();
    }

    var kalipto = worklist.FirstOrDefault(w => w.SampleItemId == 8072 || w.AssociatedItemIds.Contains(8072));
    if (kalipto is not null)
        gfx10Report.AppendLine("8072 Dofus Kalipto → " + kalipto.PreviewKey.Key);
    File.WriteAllText(Path.Combine(stagingSwf, "gfx-10-validation.txt"), gfx10Report.ToString(), Encoding.UTF8);

    Console.WriteLine();
    Console.WriteLine($"UNIQUE SWF PROCESSED: {swfSummary.UniqueSwfProcessed}");
    Console.WriteLine($"OK:      {swfSummary.Ok}");
    Console.WriteLine($"REVIEW:  {swfSummary.Review}");
    Console.WriteLine($"FAILED:  {swfSummary.Failed}");
    Console.WriteLine($"NO_ICON: {swfSummary.NoIcon}");
    Console.WriteLine("Staging: " + stagingSwf);

    if (argsMap.ContainsKey("promote-swf"))
    {
        var pr = ItemIconSwfPreviewPrepService.PromoteSwfFromBatch(
            stagingSwf, library, Path.Combine(stagingSwf, "promote-swf-report.json"), includeReview: true);
        Console.WriteLine($"PROMOTED ItemsBySwf: {pr.Created.Count}");
    }

    return 0;
}

if (argsMap.ContainsKey("swf-batch"))
{
    var cachePathBatch = argsMap.GetValueOrDefault("items-cache") ?? FindLatestItemsCache();
    if (cachePathBatch is null || !File.Exists(cachePathBatch))
    {
        Console.Error.WriteLine("ERROR: caché items_es vacía.");
        return 4;
    }

    var catalogBatch = ItemIconPreviewPrepService.LoadCatalogFromCache(cachePathBatch);
    var timeoutBatch = int.TryParse(argsMap.GetValueOrDefault("timeout"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tb)
        ? Math.Clamp(tb, 5, 300)
        : 45;
    var runIdBatch = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    var stagingBatch = Path.Combine(
        ItemIconPreviewPrepService.DefaultArtifactsRoot(repoRoot),
        "swf-batch-" + runIdBatch);
    Directory.CreateDirectory(stagingBatch);

    Console.WriteLine("ItemIconSwfPreviewPrep · CONTENT.ITEMS.PREVIEW.COLLISION.FIX.1B · FULL BATCH");
    Console.WriteLine("Clips:    " + clips);
    Console.WriteLine("FFDec:    " + ffdec);
    Console.WriteLine("Inkscape: " + (string.IsNullOrWhiteSpace(inkscape) ? "(no)" : inkscape));
    Console.WriteLine("Library:  " + library);
    Console.WriteLine($"Catálogo: {cachePathBatch} · {catalogBatch.Count} items");
    Console.WriteLine();

    var batchSvc = new ItemIconSwfPreviewPrepService();
    var batchOpts = new ItemIconSwfPreviewPrepOptions
    {
        FfdecCliPath = ffdec,
        ClipsRoot = clips,
        StagingRoot = stagingBatch,
        LibraryRoot = library,
        InkscapeCliPath = string.IsNullOrWhiteSpace(inkscape) ? null : inkscape,
        ProcessTimeout = TimeSpan.FromSeconds(timeoutBatch),
        PromoteOkToLibrary = false,
        SkipExistingLibraryPng = argsMap.ContainsKey("force") ? false : true,
    };

    var batchSummary = batchSvc.ProcessFullCatalog(batchOpts, catalogBatch);
    ItemIconSwfPreviewPrepService.WriteManifest(batchSummary, Path.Combine(stagingBatch, "manifest-native.json"));
    ItemIconSwfPreviewPrepService.WriteSummaryText(batchSummary, Path.Combine(stagingBatch, "summary.txt"));

    Console.WriteLine();
    Console.WriteLine($"Items totales:     {batchSummary.TotalCatalogItems}");
    Console.WriteLine($"SWF únicos:        {batchSummary.UniqueSwfInCatalog}");
    Console.WriteLine($"PreviewKey proc:   {batchSummary.UniqueSwfProcessed}");
    Console.WriteLine($"OK:                {batchSummary.Ok}");
    Console.WriteLine($"REVIEW:            {batchSummary.Review}");
    Console.WriteLine($"FAILED:            {batchSummary.Failed}");
    Console.WriteLine($"NO_ICON:           {batchSummary.NoIcon}");
    Console.WriteLine("Methods: " + string.Join(", ", batchSummary.ExportMethodCounts.Select(kv => $"{kv.Key}={kv.Value}")));
    Console.WriteLine("Staging: " + stagingBatch);

    if (argsMap.ContainsKey("promote-swf"))
    {
        var pr = ItemIconSwfPreviewPrepService.PromoteSwfFromBatch(
            stagingBatch, library, Path.Combine(stagingBatch, "promote-report.json"), includeReview: true);
        Console.WriteLine();
        Console.WriteLine($"PROMOTED ItemsBySwf: {pr.Created.Count}");
        Console.WriteLine("Report: " + Path.Combine(stagingBatch, "promote-report.json"));
    }

    return 0;
}

var mode = (argsMap.GetValueOrDefault("mode") ?? "sample").Trim().ToLowerInvariant();
var timeoutSec = int.TryParse(argsMap.GetValueOrDefault("timeout"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)
    ? Math.Clamp(t, 5, 300)
    : 45;
var sampleCount = int.TryParse(argsMap.GetValueOrDefault("count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c)
    ? Math.Clamp(c, 5, 100)
    : 25;
var promote = argsMap.ContainsKey("promote");

Console.WriteLine("PrepareItemIconPreviews · DEV-ONLY · CONTENT.ITEMS.PREVIEW.1B");
Console.WriteLine("Clips:    " + clips);
Console.WriteLine("FFDec:    " + ffdec);
Console.WriteLine("Inkscape: " + (string.IsNullOrWhiteSpace(inkscape) ? "(no)" : inkscape));
Console.WriteLine("Library:  " + library);
Console.WriteLine("Mode:     " + mode);
Console.WriteLine();

var cachePath = argsMap.GetValueOrDefault("items-cache") ?? FindLatestItemsCache();
IReadOnlyList<ItemIconPreviewSample> catalog = Array.Empty<ItemIconPreviewSample>();
if (cachePath is not null && File.Exists(cachePath))
{
    catalog = ItemIconPreviewPrepService.LoadCatalogFromCache(cachePath);
    Console.WriteLine($"Catálogo: {cachePath} · {catalog.Count} items");
}

IReadOnlyList<ItemIconPreviewSample> samples;
if (argsMap.TryGetValue("ids", out var idsArg) && !string.IsNullOrWhiteSpace(idsArg))
{
    samples = ParseGfxIds(idsArg, clips);
}
else if (mode is "catalog" or "full")
{
    if (catalog.Count == 0)
    {
        Console.Error.WriteLine("ERROR: caché items_es vacía.");
        return 4;
    }

    samples = mode == "full"
        ? ItemIconPreviewPrepService.BuildUniqueGfxWorklist(catalog, clips)
        : ItemIconPreviewPrepService.SelectSamplesFromCatalog(catalog, clips, sampleCount);
}
else
{
    samples = ItemIconPreviewPrepService.DefaultCalibrationSamples
        .Select(x => new ItemIconPreviewSample
        {
            ItemId = x.ItemId,
            GfxId = x.GfxId,
            TypeId = x.TypeId,
            IconRelativePath = VisualClipPaths.ItemIconRelative(x.GfxId),
        })
        .ToList();
}

if (samples.Count == 0)
{
    Console.Error.WriteLine("ERROR: muestra vacía.");
    return 5;
}

var uniqueGfx = samples.Select(s => s.GfxId).Distinct().Count();
Console.WriteLine($"Procesar: {samples.Count} gfx únicos (dedupe interno)");
if (catalog.Count > 0)
{
    var catalogGfx = catalog.Where(x => x.GfxId > 0).Select(x => x.GfxId).Distinct().Count();
    Console.WriteLine($"Catálogo total: {catalog.Count} items · {catalogGfx} gfx únicos");
}

Console.WriteLine();
var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var stagingRoot = Path.Combine(
    ItemIconPreviewPrepService.DefaultArtifactsRoot(repoRoot),
    (mode == "full" ? "batch-" : "sample-") + runId);
Directory.CreateDirectory(stagingRoot);

var svc = new ItemIconPreviewPrepService();
var opts = new ItemIconPreviewPrepOptions
{
    FfdecCliPath = ffdec,
    ClipsRoot = clips,
    StagingRoot = stagingRoot,
    LibraryRoot = library,
    InkscapeCliPath = string.IsNullOrWhiteSpace(inkscape) ? null : inkscape,
    ProcessTimeout = TimeSpan.FromSeconds(timeoutSec),
    PromoteOkToLibrary = false,
    SkipExistingLibraryPng = !argsMap.ContainsKey("force"),
};

ItemIconPreviewPrepSummary summary = mode == "full" && catalog.Count > 0
    ? svc.ProcessFullCatalog(opts, catalog)
    : svc.ProcessSamples(opts, samples, catalog.Count > 0 ? catalog : null);

ItemIconPreviewPrepService.WriteManifest(summary, Path.Combine(stagingRoot, "manifest-native.json"));
ItemIconPreviewPrepService.WriteSummaryText(summary, Path.Combine(stagingRoot, "summary.txt"));

Console.WriteLine();
Console.WriteLine($"TOTAL ITEMS:     {summary.TotalCatalogItems}");
Console.WriteLine($"UNIQUE GFX:      {summary.UniqueGfxInCatalog}");
Console.WriteLine($"PROCESSED:       {summary.UniqueGfxProcessed}");
Console.WriteLine($"SKIPPED_DUP:     {summary.SkippedDuplicateGfx}");
Console.WriteLine($"SKIPPED_LIBRARY: {summary.SkippedExistingLibrary}");
Console.WriteLine($"OK:              {summary.Ok}");
Console.WriteLine($"REVIEW:          {summary.Review}");
Console.WriteLine($"FAILED:          {summary.Failed}");
Console.WriteLine($"NO_ICON:         {summary.NoIcon}");
Console.WriteLine("Methods: " + string.Join(", ", summary.ExportMethodCounts.Select(kv => $"{kv.Key}={kv.Value}")));
Console.WriteLine();
Console.WriteLine("Staging: " + stagingRoot);

if (promote)
{
    var reportPath = Path.Combine(stagingRoot, "promote-report.json");
    var pr = ItemIconPreviewPrepService.PromoteFromBatch(stagingRoot, library, reportPath, includeReview: true);
    Console.WriteLine();
    Console.WriteLine($"PROMOTED: {pr.Created.Count} → Library/Visuals/Items/");
    Console.WriteLine("Report: " + reportPath);
}

return 0;

static void PrintHelp()
{
    Console.WriteLine("""
        PrepareItemIconPreviews (DEV-ONLY) — CONTENT.ITEMS.PREVIEW.1B

          --audit-collision      collision audit only (no batch/promote)
          --collision-fix-sample SWF-keyed collision fix sample (ItemsBySwf staging)
          --swf-batch            full catalog batch by ResolvedSwfRelativePath
          --promote-swf          promote OK+REVIEW to Library/Visuals/ItemsBySwf/
          --promote-swf-from <batch>
          --batch-manifest <path>
          --promote-from <batch>   promote OK+REVIEW from existing batch
          --promote                promote after processing this run
          --force                  reprocess even if Library PNG exists
          --clips --ffdec --inkscape --library
          --items-cache <path>
          --count 25
          --ids gfx:type,gfx:type,...
          --timeout 45
        """);
}

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (!a.StartsWith("--", StringComparison.Ordinal) && !a.StartsWith('-'))
            continue;
        var key = a.TrimStart('-');
        if (i + 1 < argv.Length && !argv[i + 1].StartsWith('-'))
            map[key] = argv[++i];
        else
            map[key] = "true";
    }

    return map;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "src", "RufusMapEditor.LegacyCompatibility")))
            return dir.FullName;
        dir = dir.Parent;
    }

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}

static SettingsDto? TryLoadSettings()
{
    var path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RufusMapEditor", "settings.json");
    if (!File.Exists(path)) return null;
    try { return JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(path)); }
    catch { return null; }
}

static string? FindLatestItemsCache()
{
    var dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RufusMapEditor", "visual-library-cache");
    if (!Directory.Exists(dir)) return null;
    return Directory.GetFiles(dir, "items_v*.json")
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .FirstOrDefault();
}

static IReadOnlyList<ItemIconPreviewSample> ParseGfxIds(string arg, string clipsRoot)
{
    var list = new List<ItemIconPreviewSample>();
    foreach (var part in arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var bits = part.Split(':');
        if (!int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gfx))
            continue;
        var typeId = bits.Length > 1
            && int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tid)
            ? tid
            : 0;
        var (rel, full, _) = VisualClipPaths.ResolveItemIcon(clipsRoot, gfx, typeId);
        list.Add(new ItemIconPreviewSample
        {
            ItemId = 0,
            GfxId = gfx,
            TypeId = typeId,
            IconRelativePath = rel,
            IconFullPath = full,
        });
    }

    return list;
}

file sealed class SettingsDto
{
    public string? ClipsRootPath { get; set; }
}
