using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;
using SkiaSharp;
using Svg.Skia;
using SvgRasterizerPoc;

// CONTENT.ITEMS.SVG_RASTERIZER — POC / integrate sample entrypoints (not ADMIN).

Console.OutputEncoding = Encoding.UTF8;

if (args.Any(a => string.Equals(a, "--integrate-sample", StringComparison.OrdinalIgnoreCase)))
    return IntegratePipelineSample.Run(args);

if (args.Any(a => string.Equals(a, "--runtime-tools-sample", StringComparison.OrdinalIgnoreCase)))
    return RuntimeToolsSample.Run(args);

// CONTENT.ITEMS.SVG_RASTERIZER.PAINT_POC.3 — paint/AA/metric analysis; not ADMIN.

var repoRoot = FindRepoRoot();
var inkscape = InkscapeCliProcessRunner.TryDiscoverInkscapeCli();
if (string.IsNullOrWhiteSpace(inkscape))
{
    Console.Error.WriteLine("ERROR: Inkscape no encontrado.");
    return 2;
}

var ffdec = Path.Combine(repoRoot, "tools", "_ffdec", "extracted", "ffdec-cli.exe");
if (!File.Exists(ffdec))
    ffdec = Path.Combine(repoRoot, "tools", "_ffdec", "ffdec-cli.exe");

var lib = Path.Combine(repoRoot, "Library", "Visuals", "ItemsBySwf");
var outRoot = Path.Combine(
    repoRoot, "tests", "tools", "artifacts", "item-icon-prep",
    "svg-rasterizer-paint-poc-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
Directory.CreateDirectory(outRoot);

var clips = ResolveClips();
var cases = new (string Key, bool Control)[]
{
    ("items/82/1.swf", false),
    ("items/82/10.swf", false),
    ("items/82/11.swf", false),
    ("items/10/13.swf", false),
    ("items/10/15.swf", false),
    ("items/5/19.swf", false),
    ("items/11/92.swf", true),
    ("items/114/2.swf", true),
    ("items/10/16.swf", true),
};

Console.WriteLine("PAINT POC.3");
Console.WriteLine("Out: " + outRoot);
Console.WriteLine("Inkscape: " + inkscape);
File.WriteAllText(Path.Combine(outRoot, "skia-config.txt"), BuildSkiaConfigDoc());

var inkRunner = new InkscapeCliProcessRunner();
var ffdecRunner = new FfdecCliProcessRunner();
var timeout = TimeSpan.FromSeconds(90);
var reports = new List<CaseReport>();
var ssFactors = new[] { 1f, 2f, 4f };

foreach (var (key, control) in cases)
{
    var stem = key.Replace('/', '_').Replace(".swf", "", StringComparison.OrdinalIgnoreCase);
    var caseDir = Path.Combine(outRoot, stem);
    Directory.CreateDirectory(caseDir);
    Console.WriteLine();
    Console.WriteLine("=== " + key + (control ? " [CONTROL]" : "") + " ===");

    var swf = Path.Combine(clips!, key.Replace('/', Path.DirectorySeparatorChar));
    var refFinal = Path.Combine(lib, stem + ".png");
    if (!File.Exists(swf) || !File.Exists(refFinal))
    {
        Console.WriteLine("SKIP missing files");
        continue;
    }

    var svgDir = Path.Combine(caseDir, "svg");
    Directory.CreateDirectory(svgDir);
    var svgRun = ffdecRunner.RunExportFrameSvg(ffdec, swf, svgDir, timeout);
    var svgPath = FfdecExportLocator.FindFirstSvg(svgDir);
    if (svgPath is null || svgRun.ExitCode != 0)
    {
        Console.WriteLine("FFDec fail");
        continue;
    }

    var expandedPath = Path.Combine(svgDir, "expanded.svg");
    var expand = SvgFrameBoundsExpander.ExpandFile(svgPath, expandedPath);
    var stage = TryReadSvgViewBox(File.ReadAllText(svgPath)) ?? new StageBox(0, 0, 550, 400);
    SvgFrameBoundsExpander.Bounds? content = expand.Ok && expand.ContentBounds.IsValid ? expand.ContentBounds : null;

    // --- A) RAW Inkscape (expanded + export-area-drawing, same as pipeline) ---
    var inkRaw = Path.Combine(caseDir, "A_inkscape_raw.png");
    var ink = inkRunner.RasterizePng(inkscape, expandedPath, inkRaw, exportAreaDrawing: true, timeout);
    if (!ink.PngProduced)
    {
        // fallback original
        ink = inkRunner.RasterizePng(inkscape, svgPath, inkRaw, exportAreaDrawing: true, timeout);
    }

    // --- A') Skia RAW at 1x/2x/4x ---
    var skiaRaws = new Dictionary<float, string>();
    foreach (var ss in ssFactors)
    {
        var path = Path.Combine(caseDir, $"A_skia_raw_ss{ss:0}.png");
        if (TryRasterizeSkia(svgPath, path, content, stage, margin: 4f, supersample: ss, out var meta, out var err))
        {
            skiaRaws[ss] = path;
            File.WriteAllText(Path.Combine(caseDir, $"A_skia_raw_ss{ss:0}.meta.txt"), meta);
        }
        else
            Console.WriteLine("Skia ss" + ss + " fail: " + err);
    }

    if (!File.Exists(inkRaw) || !skiaRaws.ContainsKey(1f))
    {
        Console.WriteLine("Missing RAW pair");
        continue;
    }

    // --- B) Normalized ---
    var inkNorm = Path.Combine(caseDir, "B_inkscape_norm.png");
    var skiaNorm1 = Path.Combine(caseDir, "B_skia_norm_ss1.png");
    NpcGfxPreviewCropNormalizer.NormalizeFile(inkRaw, inkNorm);
    NpcGfxPreviewCropNormalizer.NormalizeFile(skiaRaws[1f], skiaNorm1);

    var skiaNormSs = new Dictionary<float, string> { [1f] = skiaNorm1 };
    foreach (var ss in ssFactors.Where(x => x > 1 && skiaRaws.ContainsKey(x)))
    {
        var p = Path.Combine(caseDir, $"B_skia_norm_ss{ss:0}.png");
        NpcGfxPreviewCropNormalizer.NormalizeFile(skiaRaws[ss], p);
        skiaNormSs[ss] = p;
    }

    // Library final (Inkscape pipeline historically) as C reference
    var libFinal = Path.Combine(caseDir, "C_library_final.png");
    File.Copy(refFinal, libFinal, true);
    File.Copy(skiaNorm1, Path.Combine(caseDir, "C_skia_final_ss1.png"), true);

    // Diffs
    WriteDiff(inkRaw, skiaRaws[1f], Path.Combine(caseDir, "DIFF_raw_ink_vs_skia_ss1.png"));
    WriteDiff(inkNorm, skiaNorm1, Path.Combine(caseDir, "DIFF_norm_ink_vs_skia_ss1.png"));
    WriteDiff(libFinal, skiaNorm1, Path.Combine(caseDir, "DIFF_lib_vs_skia_ss1.png"));

    var rawCmp = CompareDetailed(inkRaw, skiaRaws[1f]);
    var normCmp = CompareDetailed(inkNorm, skiaNorm1);
    var libCmp = CompareDetailed(libFinal, skiaNorm1);

    var ssResults = new List<object>();
    foreach (var ss in ssFactors.Where(skiaNormSs.ContainsKey))
    {
        var c = CompareDetailed(inkNorm, skiaNormSs[ss]);
        ssResults.Add(new
        {
            Supersample = ss,
            DiffRatio = c.DiffRatio,
            OpaqueDelta = c.OpaqueDelta,
            OnlyRef = c.OnlyRef,
            OnlyCand = c.OnlyCand,
            MeanAbs = c.MeanAbs,
            VisualClassHint = c.DiffRatio <= 0.04 ? "MATCH-ish" : c.DiffRatio <= 0.12 ? "OK-ish" : c.DiffRatio <= 0.22 ? "REVIEW-ish" : "FAILED-ish",
        });
        Console.WriteLine($"  SS{ss}: normDiff={c.DiffRatio:0.####} onlyRef={c.OnlyRef} onlyCand={c.OnlyCand}");
    }

    // Visual judgment helpers
    var missingMass = normCmp.OnlyRef > 800 && normCmp.OnlyRef > normCmp.OnlyCand * 3 && normCmp.OpaqueDelta > 0.15;
    var metricFailed = ClassifyMetric(normCmp) == "FAILED";
    var visualNote = InferVisualNote(stem, normCmp, missingMass);

    var report = new CaseReport
    {
        PreviewKey = key,
        Stem = stem,
        Control = control,
        ContentBounds = content is { } cb
            ? $"{cb.MinX:0.##},{cb.MinY:0.##},{cb.MaxX:0.##},{cb.MaxY:0.##}"
            : null,
        Raw = ToDto(rawCmp),
        Normalized = ToDto(normCmp),
        VsLibraryFinal = ToDto(libCmp),
        SupersampleNorm = ssResults,
        MetricClassNorm = ClassifyMetric(normCmp),
        MetricClassRaw = ClassifyMetric(rawCmp),
        SuspectedMissingMass = missingMass,
        VisualJudgment = visualNote.Judgment,
        VisualReason = visualNote.Reason,
        DivergenceStartsAt = rawCmp.DiffRatio <= 0.08 && normCmp.DiffRatio > rawCmp.DiffRatio + 0.05
            ? "NORMALIZER amplifica"
            : rawCmp.DiffRatio > 0.12
                ? "RAW (rasterizado)"
                : normCmp.DiffRatio > 0.12
                    ? "NORMALIZER / escala final"
                    : "diferencias menores (AA/subpixel)",
    };

    File.WriteAllText(
        Path.Combine(caseDir, "metrics.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"  RAW  diff={rawCmp.DiffRatio:0.####} onlyRef={rawCmp.OnlyRef} onlyCand={rawCmp.OnlyCand} ink={rawCmp.InkBoundsA} skia={rawCmp.InkBoundsB}");
    Console.WriteLine($"  NORM diff={normCmp.DiffRatio:0.####} onlyRef={normCmp.OnlyRef} onlyCand={normCmp.OnlyCand} metric={report.MetricClassNorm}");
    Console.WriteLine($"  VISUAL={report.VisualJudgment} · {report.VisualReason}");
    Console.WriteLine($"  DIVERGE@{report.DivergenceStartsAt}");

    reports.Add(report);
}

File.WriteAllText(
    Path.Combine(outRoot, "paint-report.json"),
    JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));

WriteSummary(outRoot, reports);
Console.WriteLine();
Console.WriteLine("DONE " + outRoot);
return 0;

static string BuildSkiaConfigDoc() => """
PAINT_POC.3 — configuración Skia usada
- SKColorType: Rgba8888
- SKAlphaType: Unpremul (no premultiplied)
- Clear: Transparent
- DrawPicture: scale + Translate(-left,-top)
- Antialias: default SKPicture / SVG path (no override explícito)
- FilterQuality: default (no SKSamplingOptions override)
- DPI: no (bitmap pixel grid)
- Canvas: Stage ∪ ContentBounds ∪ CullRect + margin=4
- Supersample Nx: multiplica tamaño del bitmap (scale *= N) antes de encode; el normalizador reduce a 256
""";

static (string Judgment, string Reason) InferVisualNote(string stem, DetailedCmp norm, bool missingMass)
{
    // Conservative automatic hints; human image review overrides in delivery.
    if (missingMass)
        return ("FAILED_VISUAL_SUSPECT", "onlyRef alto + opaqueDelta sugiere masa ausente");
    if (norm.DiffRatio <= 0.05 && norm.OpaqueDelta <= 0.12)
        return ("MATCH_VISUAL", "diff/opaque bajos");
    if (norm.DiffRatio <= 0.12 && norm.OnlyCand < 200)
        return ("OK_VISUAL", "diferencias menores; composición probablemente intacta");
    if (norm.OnlyRef > 500 && norm.OnlyCand == 0 && norm.OpaqueDelta < 0.08)
        return ("FAILED_METRIC_LIKELY", "onlyCand=0 + opaqueDelta bajo → AA/escala/posición, no pieza ausente");
    if (norm.DiffRatio <= 0.22)
        return ("REVIEW_VISUAL", "diff visible; revisar composición");
    return ("FAILED_VISUAL_SUSPECT", "diff alto");
}

static string ClassifyMetric(DetailedCmp c)
{
    if (c.OpaqueB < 20) return "FAILED";
    // AA/subpíxel (p.ej. 82/1, 82/10): onlyRef alto + onlyCand≈0 + intersección>95% ≠ FAILED visual
    var intersectPct = c.OpaqueA == 0 ? 0.0 : c.Both / (double)c.OpaqueA;
    if (c.OnlyRef > 400 && c.OnlyCand <= 80 && intersectPct > 0.95 && c.OpaqueDelta < 0.18)
        return c.DiffRatio <= 0.12 ? "OK" : "REVIEW";
    if (c.OpaqueDelta > 0.35 || (c.OnlyRef > 800 && c.OnlyRef > c.OnlyCand * 3 && intersectPct <= 0.95))
        return "FAILED";
    if (c.DiffRatio <= 0.04 && c.OpaqueDelta <= 0.12) return "MATCH";
    if (c.DiffRatio <= 0.10 && c.OpaqueDelta <= 0.25) return "OK";
    if (c.DiffRatio <= 0.22 && c.OpaqueDelta <= 0.40) return "REVIEW";
    return "FAILED";
}

static object ToDto(DetailedCmp c) => new
{
    c.DiffRatio,
    c.OpaqueDelta,
    c.OnlyRef,
    c.OnlyCand,
    c.Both,
    c.MeanAbs,
    c.OpaqueA,
    c.OpaqueB,
    OnlyRefPctOfRef = c.OpaqueA == 0 ? 0 : Math.Round(c.OnlyRef / (double)c.OpaqueA, 4),
    OnlyCandPctOfCand = c.OpaqueB == 0 ? 0 : Math.Round(c.OnlyCand / (double)c.OpaqueB, 4),
    IntersectPctOfRef = c.OpaqueA == 0 ? 0 : Math.Round(c.Both / (double)c.OpaqueA, 4),
    InkBoundsRef = c.InkBoundsA,
    InkBoundsCand = c.InkBoundsB,
    CenterRef = c.CenterA,
    CenterCand = c.CenterB,
    SizeRef = c.SizeA,
    SizeCand = c.SizeB,
    ScaleRatioW = c.InkW_A == 0 ? 0 : Math.Round(c.InkW_B / (double)c.InkW_A, 4),
    ScaleRatioH = c.InkH_A == 0 ? 0 : Math.Round(c.InkH_B / (double)c.InkH_A, 4),
};

static void WriteSummary(string outRoot, List<CaseReport> reports)
{
    var sb = new StringBuilder();
    sb.AppendLine("PAINT_POC.3 SUMMARY");
    foreach (var r in reports)
    {
        sb.AppendLine(
            $"{r.PreviewKey}: metricNorm={r.MetricClassNorm} metricRaw={r.MetricClassRaw} visual={r.VisualJudgment} diverge@{r.DivergenceStartsAt} · {r.VisualReason}");
    }

    var match = reports.Count(r => r.VisualJudgment.StartsWith("MATCH", StringComparison.Ordinal));
    var ok = reports.Count(r => r.VisualJudgment.StartsWith("OK", StringComparison.Ordinal));
    var review = reports.Count(r => r.VisualJudgment.StartsWith("REVIEW", StringComparison.Ordinal));
    var failVis = reports.Count(r => r.VisualJudgment.Contains("FAILED_VISUAL", StringComparison.Ordinal));
    var failMet = reports.Count(r => r.VisualJudgment.Contains("FAILED_METRIC", StringComparison.Ordinal));
    sb.AppendLine($"VISUAL_HINT MATCH={match} OK={ok} REVIEW={review} FAILED_VISUAL={failVis} FAILED_METRIC={failMet}");
    File.WriteAllText(Path.Combine(outRoot, "summary.txt"), sb.ToString());
}

static bool TryRasterizeSkia(
    string svgPath,
    string pngPath,
    SvgFrameBoundsExpander.Bounds? content,
    StageBox stage,
    float margin,
    float supersample,
    out string meta,
    out string? error)
{
    error = null;
    meta = "";
    try
    {
        using var svg = new SKSvg();
        svg.Load(svgPath);
        var picture = svg.Picture;
        if (picture is null)
        {
            error = "Picture null";
            return false;
        }

        var cull = picture.CullRect;
        float left = stage.MinX, top = stage.MinY, right = stage.MaxX, bottom = stage.MaxY;
        void Incl(float x0, float y0, float x1, float y1)
        {
            left = Math.Min(left, x0);
            top = Math.Min(top, y0);
            right = Math.Max(right, x1);
            bottom = Math.Max(bottom, y1);
        }

        if (!cull.IsEmpty && cull.Width > 0 && cull.Height > 0)
            Incl(cull.Left, cull.Top, cull.Right, cull.Bottom);
        if (content is { IsValid: true } cb)
            Incl((float)cb.MinX, (float)cb.MinY, (float)cb.MaxX, (float)cb.MaxY);
        left -= margin;
        top -= margin;
        right += margin;
        bottom += margin;

        var fullW = right - left;
        var fullH = bottom - top;
        var scale = Math.Max(0.01f, supersample);
        const float maxEdge = 4096f;
        if (fullW * scale > maxEdge || fullH * scale > maxEdge)
            scale = maxEdge / Math.Max(fullW, fullH);

        var w = Math.Max(1, (int)Math.Ceiling(fullW * scale));
        var h = Math.Max(1, (int)Math.Ceiling(fullH * scale));
        using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            // Default paint antialias comes from picture ops; enable high-quality if drawing extras.
            canvas.Scale(scale);
            canvas.Translate(-left, -top);
            canvas.DrawPicture(picture);
            canvas.Flush();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null) { error = "encode null"; return false; }
        using (var fs = File.Open(pngPath, FileMode.Create, FileAccess.Write))
            data.SaveTo(fs);

        meta = string.Create(CultureInfo.InvariantCulture,
            $"canvas={left:0.##},{top:0.##},{right:0.##},{bottom:0.##} bmp={w}x{h} scale={scale:0.###} color=Rgba8888 alpha=Unpremul ss={supersample}");
        return true;
    }
    catch (Exception ex)
    {
        error = ex.GetType().Name + ": " + ex.Message;
        return false;
    }
}

static DetailedCmp CompareDetailed(string aPath, string bPath)
{
    using var a = new Bitmap(aPath);
    using var bSrc = new Bitmap(bPath);
    Bitmap b = bSrc;
    Bitmap? resized = null;
    if (a.Width != bSrc.Width || a.Height != bSrc.Height)
    {
        resized = new Bitmap(a.Width, a.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(resized);
        g.Clear(Color.Transparent);
        g.DrawImage(bSrc, 0, 0, a.Width, a.Height);
        b = resized;
    }

    try
    {
        var inkA = MeasureInk(a);
        var inkB = MeasureInk(b);
        var diff = 0;
        var opaqueA = 0;
        var opaqueB = 0;
        var onlyA = 0;
        var onlyB = 0;
        var both = 0;
        long sumAbs = 0;
        var pixels = a.Width * a.Height;
        for (var y = 0; y < a.Height; y++)
        for (var x = 0; x < a.Width; x++)
        {
            var ca = a.GetPixel(x, y);
            var cb = b.GetPixel(x, y);
            var oa = ca.A >= 16;
            var ob = cb.A >= 16;
            if (oa) opaqueA++;
            if (ob) opaqueB++;
            if (oa && ob) both++;
            else if (oa) onlyA++;
            else if (ob) onlyB++;
            var d = Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B) + Math.Abs(ca.A - cb.A);
            sumAbs += d;
            if (d > 40) diff++;
        }

        var opaqueDelta = opaqueA == 0 ? 0 : Math.Abs(opaqueA - opaqueB) / (double)opaqueA;
        return new DetailedCmp(
            DiffRatio: diff / (double)pixels,
            OpaqueDelta: opaqueDelta,
            OnlyRef: onlyA,
            OnlyCand: onlyB,
            Both: both,
            MeanAbs: sumAbs / (pixels * 4.0),
            OpaqueA: opaqueA,
            OpaqueB: opaqueB,
            InkBoundsA: inkA.Bounds,
            InkBoundsB: inkB.Bounds,
            CenterA: inkA.Center,
            CenterB: inkB.Center,
            SizeA: $"{a.Width}x{a.Height}",
            SizeB: $"{b.Width}x{b.Height}",
            InkW_A: inkA.W,
            InkH_A: inkA.H,
            InkW_B: inkB.W,
            InkH_B: inkB.H);
    }
    finally
    {
        resized?.Dispose();
    }
}

static (string Bounds, string Center, int W, int H) MeasureInk(Bitmap bmp)
{
    var minX = bmp.Width;
    var minY = bmp.Height;
    var maxX = -1;
    var maxY = -1;
    for (var y = 0; y < bmp.Height; y++)
    for (var x = 0; x < bmp.Width; x++)
    {
        if (bmp.GetPixel(x, y).A < 16) continue;
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

    if (maxX < 0)
        return ("(empty)", "(none)", 0, 0);
    var w = maxX - minX + 1;
    var h = maxY - minY + 1;
    var cx = minX + w / 2.0;
    var cy = minY + h / 2.0;
    return ($"{minX},{minY},{maxX},{maxY}", $"{cx:0.#},{cy:0.#}", w, h);
}

static void WriteDiff(string aPath, string bPath, string outPath)
{
    using var a = new Bitmap(aPath);
    using var bSrc = new Bitmap(bPath);
    using var b = a.Width == bSrc.Width && a.Height == bSrc.Height
        ? new Bitmap(bSrc)
        : new Bitmap(a.Width, a.Height, PixelFormat.Format32bppArgb);
    if (a.Width != bSrc.Width || a.Height != bSrc.Height)
    {
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        g.DrawImage(bSrc, 0, 0, a.Width, a.Height);
    }

    using var diff = new Bitmap(a.Width, a.Height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < a.Height; y++)
    for (var x = 0; x < a.Width; x++)
    {
        var ca = a.GetPixel(x, y);
        var cb = b.GetPixel(x, y);
        var d = Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B) + Math.Abs(ca.A - cb.A);
        var v = (byte)Math.Clamp(d / 4, 0, 255);
        // Magenta = only ref, cyan = only cand, gray = both differ
        var oa = ca.A >= 16;
        var ob = cb.A >= 16;
        if (oa && !ob) diff.SetPixel(x, y, Color.FromArgb(255, 255, 0, 255));
        else if (!oa && ob) diff.SetPixel(x, y, Color.FromArgb(255, 0, 255, 255));
        else if (d > 40) diff.SetPixel(x, y, Color.FromArgb(255, v, v, v));
        else diff.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    diff.Save(outPath, ImageFormat.Png);
}

static StageBox? TryReadSvgViewBox(string svgText)
{
    try
    {
        var doc = new XmlDocument { XmlResolver = null };
        doc.LoadXml(svgText);
        var root = doc.DocumentElement;
        if (root is null) return null;
        var vb = root.GetAttribute("viewBox");
        if (string.IsNullOrWhiteSpace(vb)) return new StageBox(0, 0, 550, 400);
        var parts = Regex.Split(vb.Trim(), @"[\s,]+");
        if (parts.Length >= 4
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            return new StageBox(x, y, x + w, y + h);
    }
    catch { /* ignore */ }

    return new StageBox(0, 0, 550, 400);
}

static string? ResolveClips()
{
    var settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RufusMapEditor", "settings.json");
    if (File.Exists(settingsPath))
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (doc.RootElement.TryGetProperty("ClipsRootPath", out var p))
            {
                var v = p.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                {
                    var n = ClipsRootPaths.Validate(v).NormalizedPath ?? v;
                    if (Directory.Exists(n)) return n;
                }
            }
        }
        catch { /* ignore */ }
    }

    return ClipsRootPaths.TryDiscoverUnambiguous();
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 12 && !string.IsNullOrWhiteSpace(dir); i++)
    {
        if (Directory.Exists(Path.Combine(dir, "Library", "Visuals")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName ?? "";
    }

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}

sealed record StageBox(float MinX, float MinY, float MaxX, float MaxY);

readonly record struct DetailedCmp(
    double DiffRatio,
    double OpaqueDelta,
    int OnlyRef,
    int OnlyCand,
    int Both,
    double MeanAbs,
    int OpaqueA,
    int OpaqueB,
    string InkBoundsA,
    string InkBoundsB,
    string CenterA,
    string CenterB,
    string SizeA,
    string SizeB,
    int InkW_A,
    int InkH_A,
    int InkW_B,
    int InkH_B);

sealed class CaseReport
{
    public string PreviewKey { get; set; } = "";
    public string Stem { get; set; } = "";
    public bool Control { get; set; }
    public string? ContentBounds { get; set; }
    public object? Raw { get; set; }
    public object? Normalized { get; set; }
    public object? VsLibraryFinal { get; set; }
    public List<object>? SupersampleNorm { get; set; }
    public string MetricClassNorm { get; set; } = "";
    public string MetricClassRaw { get; set; } = "";
    public bool SuspectedMissingMass { get; set; }
    public string VisualJudgment { get; set; } = "";
    public string VisualReason { get; set; } = "";
    public string DivergenceStartsAt { get; set; } = "";
}
