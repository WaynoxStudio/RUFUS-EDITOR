using System.Globalization;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

// ADMIN.UI.4B.2A.3G.1 — DEV-ONLY FFDec prep for NPC artwork PNGs.
// Never ships in USER/ADMIN dist. Never writes BD / SWF / client.
Console.OutputEncoding = Encoding.UTF8;

var argsMap = ParseArgs(args);
if (argsMap.ContainsKey("help") || argsMap.ContainsKey("h") || argsMap.ContainsKey("?"))
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
    Console.Error.WriteLine("ERROR: clips root inválido. Usa --clips o configura AppSettings.ClipsRootPath.");
    Console.Error.WriteLine(clipsValidation.Message);
    return 2;
}

clips = clipsValidation.NormalizedPath;

var ffdec = argsMap.GetValueOrDefault("ffdec")
            ?? Environment.GetEnvironmentVariable("RUFUS_FFDEC_CLI")
            ?? "";
if (string.IsNullOrWhiteSpace(ffdec) || !File.Exists(ffdec))
{
    Console.Error.WriteLine("ERROR: ffdec-cli.exe no encontrado.");
    Console.Error.WriteLine("Pasa --ffdec \"C:\\ruta\\ffdec-cli.exe\" o define RUFUS_FFDEC_CLI.");
    return 3;
}

var library = argsMap.GetValueOrDefault("library")
              ?? Path.Combine(repoRoot, "Library");
var mode = (argsMap.GetValueOrDefault("mode") ?? "sample").Trim().ToLowerInvariant();
var promote = argsMap.ContainsKey("promote");
var compareZoom = !argsMap.ContainsKey("no-compare-zoom");
var timeoutSec = int.TryParse(argsMap.GetValueOrDefault("timeout"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)
    ? Math.Clamp(t, 5, 300)
    : 45;

Console.WriteLine("PrepareNpcGfxPreviews · DEV-ONLY · RO BD · sin dist");
Console.WriteLine("Clips:  " + clips);
Console.WriteLine("FFDec:  " + ffdec);
Console.WriteLine("Library:" + library);
Console.WriteLine("Mode:   " + mode + (promote ? " +promote" : " (staging only)"));
Console.WriteLine();

// --- Load confirmed NPC gfxIDs (RO) ---
IReadOnlyList<int> confirmed;
int overlapCount = -1;
IReadOnlyList<int> overlapSample = Array.Empty<int>();
try
{
    var (npcIds, mobIds) = await LoadGfxSetsAsync(settings).ConfigureAwait(false);
    confirmed = npcIds.OrderBy(x => x).ToList();
    var overlap = npcIds.Intersect(mobIds).OrderBy(x => x).ToList();
    overlapCount = overlap.Count;
    overlapSample = overlap.Take(25).ToList();
}
catch (Exception ex)
{
    Console.Error.WriteLine("ERROR BD RO (npcs_modelo): " + ex.Message);
    return 4;
}

var withArtwork = 0;
var withoutArtwork = 0;
foreach (var id in confirmed)
{
    if (File.Exists(NpcGfxPreviewPrepService.ResolveArtworkSwf(clips, id)))
        withArtwork++;
    else
        withoutArtwork++;
}

Console.WriteLine($"GFX NPC confirmados (DISTINCT gfxID>0): {confirmed.Count}");
Console.WriteLine($"Con artwork/big: {withArtwork}");
Console.WriteLine($"Sin artwork:     {withoutArtwork}");
Console.WriteLine($"Colisión gfxID mobs_modelo ∩ npcs_modelo: {overlapCount}");
if (overlapSample.Count > 0)
    Console.WriteLine("  muestra: " + string.Join(", ", overlapSample));
Console.WriteLine("Visuals/Mobs: override por gfxID compartido (MonsterPicker + ArtworkPreviewService).");
Console.WriteLine("  → mismo recurso clips/artworks/big/{gfx}.swf; promover NPC no cambia semántica de mobs_modelo.id.");
Console.WriteLine("  → MANUAL_EXISTS evita sobrescribir overrides aprobados.");
Console.WriteLine();

if (mode == "full")
{
    Console.Error.WriteLine("ABORT: --mode full deshabilitado en FASE 1. Valida muestra primero.");
    return 5;
}

var sampleIds = BuildSampleIds(confirmed, argsMap.GetValueOrDefault("ids"));
Console.WriteLine("Muestra: " + string.Join(", ", sampleIds));
Console.WriteLine();

var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var stagingRoot = Path.Combine(
    NpcGfxPreviewPrepService.DefaultArtifactsRoot(repoRoot),
    "sample-" + runId);
Directory.CreateDirectory(stagingRoot);

var svc = new NpcGfxPreviewPrepService();

// Native pass
var nativeOpts = new NpcGfxPreviewPrepOptions
{
    FfdecCliPath = ffdec,
    ClipsRoot = clips,
    StagingRoot = stagingRoot,
    LibraryRoot = library,
    GfxIds = sampleIds,
    Zoom = 1,
    ProcessTimeout = TimeSpan.FromSeconds(timeoutSec),
    PromoteOkToLibrary = false,
};
var nativeSummary = svc.ProcessMany(nativeOpts);

NpcGfxPreviewPrepSummary? zoom2Summary = null;
if (compareZoom)
{
    var zOpts = new NpcGfxPreviewPrepOptions
    {
        FfdecCliPath = ffdec,
        ClipsRoot = clips,
        StagingRoot = stagingRoot,
        LibraryRoot = library,
        GfxIds = sampleIds,
        Zoom = 2,
        ProcessTimeout = TimeSpan.FromSeconds(timeoutSec),
        PromoteOkToLibrary = false,
    };
    zoom2Summary = svc.ProcessMany(zOpts);
}

var zoomNote = BuildZoomRecommendation(nativeSummary, zoom2Summary);
nativeSummary = new NpcGfxPreviewPrepSummary
{
    ConfirmedGfxCount = confirmed.Count,
    WithArtwork = withArtwork,
    WithoutArtwork = withoutArtwork,
    Processed = nativeSummary.Processed,
    Ok = nativeSummary.Ok,
    Review = nativeSummary.Review,
    Failed = nativeSummary.Failed,
    NoArtwork = nativeSummary.NoArtwork,
    ManualExists = nativeSummary.ManualExists,
    Entries = nativeSummary.Entries,
    StagingRoot = stagingRoot,
    RecommendedZoomNote = zoomNote,
};

var manifestPath = Path.Combine(stagingRoot, "manifest-native.json");
NpcGfxPreviewPrepService.WriteManifest(nativeSummary, manifestPath);
NpcGfxPreviewPrepService.WriteIndexHtml(nativeSummary, Path.Combine(stagingRoot, "index.html"));

if (zoom2Summary is not null)
{
    var z2 = new NpcGfxPreviewPrepSummary
    {
        ConfirmedGfxCount = confirmed.Count,
        WithArtwork = withArtwork,
        WithoutArtwork = withoutArtwork,
        Processed = zoom2Summary.Processed,
        Ok = zoom2Summary.Ok,
        Review = zoom2Summary.Review,
        Failed = zoom2Summary.Failed,
        NoArtwork = zoom2Summary.NoArtwork,
        ManualExists = zoom2Summary.ManualExists,
        Entries = zoom2Summary.Entries,
        StagingRoot = stagingRoot,
        RecommendedZoomNote = zoomNote,
    };
    NpcGfxPreviewPrepService.WriteManifest(z2, Path.Combine(stagingRoot, "manifest-zoom2.json"));
}

WriteTextSummary(Path.Combine(stagingRoot, "summary.txt"), nativeSummary, zoom2Summary, overlapCount);

if (promote)
{
    Console.WriteLine();
    Console.WriteLine("Promoción Library (solo OK, sin sobrescribir manuals)...");
    var promoted = 0;
    var skipped = 0;
    foreach (var e in nativeSummary.Entries.Where(x => x.Status == NpcGfxPreviewPrepStatus.Ok))
    {
        var dest = NpcGfxPreviewPrepService.ResolveManualPng(library, e.GfxId);
        if (dest is null || e.OutputPng is null || !File.Exists(e.OutputPng))
            continue;
        if (File.Exists(dest))
        {
            skipped++;
            continue;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(e.OutputPng, dest, overwrite: false);
        promoted++;
        Console.WriteLine($"  PROMOTED {e.GfxId} → Visuals/Mobs/{e.GfxId}.png");
    }

    Console.WriteLine($"Promovidos={promoted} omitidos(manual)={skipped}");
}

PrintConsoleReport(nativeSummary, zoom2Summary);
Console.WriteLine();
Console.WriteLine("Staging: " + stagingRoot);
Console.WriteLine("Index:   " + Path.Combine(stagingRoot, "index.html"));
Console.WriteLine();
Console.WriteLine("DETENTE PARA REVISIÓN — no batch 251 hasta validar muestra.");
return 0;

// ----------------- helpers -----------------

static void PrintHelp()
{
    Console.WriteLine("""
        PrepareNpcGfxPreviews (DEV-ONLY)

          --mode sample|full     (default sample; full blocked in phase 1)
          --clips <path>         clips root (else AppSettings / discovery)
          --ffdec <path>         ffdec-cli.exe (else RUFUS_FFDEC_CLI)
          --library <path>       Library root (default ./Library)
          --ids 30,71,...        override sample ids
          --timeout 45           seconds per FFDec
          --promote              copy OK native PNGs to Visuals/Mobs (no overwrite)
          --no-compare-zoom      skip zoom=2 pass
          --help
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
        {
            map[key] = argv[++i];
        }
        else
        {
            map[key] = "true";
        }
    }

    return map;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "RufusMapEditor.sln"))
            || Directory.Exists(Path.Combine(dir.FullName, "src", "RufusMapEditor.LegacyCompatibility")))
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
    if (!File.Exists(path))
        return null;
    try
    {
        return JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(path));
    }
    catch
    {
        return null;
    }
}

static async Task<(HashSet<int> npc, HashSet<int> mob)> LoadGfxSetsAsync(SettingsDto? settings)
{
    if (settings?.Database is null)
        throw new InvalidOperationException("Database settings ausentes en %LocalAppData%\\RufusMapEditor\\settings.json");

    var pwd = DatabasePasswordProtector.Unprotect(settings.Database.PasswordProtectedBase64);
    var schema = string.IsNullOrWhiteSpace(settings.Database.Database)
        ? NpcsModeloColumns.DefaultDatabase
        : settings.Database.Database.Trim();

    await using var conn = new MySqlConnection(settings.Database.BuildConnectionString(pwd));
    await conn.OpenAsync().ConfigureAwait(false);

    async Task<HashSet<int>> Load(string table, string col)
    {
        var sql = $"SELECT DISTINCT `{col}` FROM `{schema}`.`{table}` WHERE `{col}` > 0";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var set = new HashSet<int>();
        while (await rd.ReadAsync().ConfigureAwait(false))
            set.Add(rd.GetInt32(0));
        return set;
    }

    var npc = await Load(NpcsModeloColumns.DefaultTable, NpcsModeloColumns.GfxId).ConfigureAwait(false);
    var mob = await Load(MobsModeloColumns.DefaultTable, MobsModeloColumns.GfxId).ConfigureAwait(false);
    return (npc, mob);
}

static List<int> BuildSampleIds(IReadOnlyList<int> confirmed, string? idsArg)
{
    var required = new[] { 30, 71, 120, 1245, 9059, 9073 };
    var set = new HashSet<int>();
    if (!string.IsNullOrWhiteSpace(idsArg))
    {
        foreach (var part in idsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                set.Add(id);
        }
    }

    // Always force required ids into sample for calibration (even if absent from BD).
    foreach (var r in required)
        set.Add(r);

    // Fill with confirmed ids across ranges until ~16–20
    var ordered = confirmed.OrderBy(x => x).ToList();
    void AddFromBand(int min, int max, int take)
    {
        foreach (var id in ordered.Where(x => x >= min && x <= max).Take(take))
            set.Add(id);
    }

    AddFromBand(1, 200, 4);
    AddFromBand(201, 2000, 4);
    AddFromBand(2001, 99999, 4);

    // Never drop calibration ids when capping sample size.
    var extras = set.Except(required).OrderBy(x => x).Take(Math.Max(0, 20 - required.Length)).ToList();
    return required.Concat(extras).Distinct().OrderBy(x => x).ToList();
}

static string BuildZoomRecommendation(NpcGfxPreviewPrepSummary native, NpcGfxPreviewPrepSummary? zoom2)
{
    if (zoom2 is null)
        return "Zoom2 no comparado.";

    static double AvgBytes(IEnumerable<NpcGfxPreviewPrepEntry> e) =>
        e.Where(x => x.Status == NpcGfxPreviewPrepStatus.Ok && x.FileBytes > 0).Select(x => (double)x.FileBytes).DefaultIfEmpty(0).Average();

    static double AvgEdge(IEnumerable<NpcGfxPreviewPrepEntry> e) =>
        e.Where(x => x.Status == NpcGfxPreviewPrepStatus.Ok).Select(x => (double)Math.Max(x.Width, x.Height)).DefaultIfEmpty(0).Average();

    var nBytes = AvgBytes(native.Entries);
    var zBytes = AvgBytes(zoom2.Entries);
    var nEdge = AvgEdge(native.Entries);
    var zEdge = AvgEdge(zoom2.Entries);
    var ratio = nBytes > 0 ? zBytes / nBytes : 0;

    // Prefer native if already ≥256 edge for gallery; zoom2 if native is tiny.
    var rec = nEdge >= 256
        ? "Recomendado: native (maestro ≥256px; zoom2 ~" + ratio.ToString("0.0", CultureInfo.InvariantCulture) + "× bytes)"
        : "Recomendado: zoom 2 (native edge medio " + nEdge.ToString("0", CultureInfo.InvariantCulture) + "px)";

    return string.Create(CultureInfo.InvariantCulture,
        $"native avgBytes={nBytes:0} avgEdge={nEdge:0} | zoom2 avgBytes={zBytes:0} avgEdge={zEdge:0} | {rec}");
}

static void WriteTextSummary(
    string path,
    NpcGfxPreviewPrepSummary native,
    NpcGfxPreviewPrepSummary? zoom2,
    int overlap)
{
    var sb = new StringBuilder();
    sb.AppendLine("ADMIN.UI.4B.2A.3G.1 sample summary");
    sb.AppendLine($"Confirmed={native.ConfirmedGfxCount} withArt={native.WithArtwork} without={native.WithoutArtwork}");
    sb.AppendLine($"Sample processed={native.Processed} OK={native.Ok} REVIEW={native.Review} FAILED={native.Failed} NO_ARTWORK={native.NoArtwork} MANUAL={native.ManualExists}");
    sb.AppendLine($"Overlap mobs∩npcs gfx={overlap}");
    sb.AppendLine(native.RecommendedZoomNote);
    sb.AppendLine();
    foreach (var e in native.Entries)
    {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{e.GfxId}\t{e.Status}\t{e.Width}x{e.Height}\tvis={e.VisibleWidth}x{e.VisibleHeight}\topaque={e.OpaquePixelCount}\t{e.Reason}"));
    }

    if (zoom2 is not null)
    {
        sb.AppendLine();
        sb.AppendLine("--- zoom2 ---");
        foreach (var e in zoom2.Entries)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{e.GfxId}\t{e.Status}\t{e.Width}x{e.Height}\tbytes={e.FileBytes}\t{e.Reason}"));
        }
    }

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
}

static void PrintConsoleReport(NpcGfxPreviewPrepSummary native, NpcGfxPreviewPrepSummary? zoom2)
{
    Console.WriteLine("=== SAMPLE NATIVE ===");
    Console.WriteLine($"OK={native.Ok} REVIEW={native.Review} FAILED={native.Failed} NO_ARTWORK={native.NoArtwork} MANUAL={native.ManualExists}");
    foreach (var id in new[] { 9059, 1245, 30, 71, 120, 9073 })
    {
        var e = native.Entries.FirstOrDefault(x => x.GfxId == id);
        if (e is null) continue;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  GFX {id}: {e.Status} {e.Width}x{e.Height} opaque={e.OpaquePixelCount} · {e.Reason}"));
    }

    if (zoom2 is not null)
    {
        Console.WriteLine();
        Console.WriteLine("=== ZOOM2 ===");
        Console.WriteLine($"OK={zoom2.Ok} REVIEW={zoom2.Review} FAILED={zoom2.Failed}");
    }

    Console.WriteLine();
    Console.WriteLine(native.RecommendedZoomNote);
}

file sealed class SettingsDto
{
    public string? ClipsRootPath { get; set; }
    public DatabaseSettings? Database { get; set; }
}
