using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>ADMIN.UI.4B.2A.3G.1/2B/2C — orchestrates FFDec (+ Svg.Skia for FullRootSvg) export → validate → staging.</summary>
public sealed class NpcGfxPreviewPrepService
{
    private readonly IFfdecProcessRunner _ffdec;
    private readonly IInkscapeSvgRasterizer _inkscape;

    public NpcGfxPreviewPrepService(
        IFfdecProcessRunner? ffdec = null,
        IInkscapeSvgRasterizer? inkscape = null)
    {
        _ffdec = ffdec ?? new FfdecCliProcessRunner();
        // Kept for binary compat / optional legacy callers; FullRootSvg uses Svg.Skia.
        _inkscape = inkscape ?? new InkscapeCliProcessRunner();
        _ = _inkscape;
    }

    private void ApplyJavaFromOptions(NpcGfxPreviewPrepOptions options)
    {
        if (_ffdec is not FfdecCliProcessRunner concrete)
            return;
        if (!string.IsNullOrWhiteSpace(options.JavaHome))
            concrete.JavaHome = options.JavaHome;
        if (!string.IsNullOrWhiteSpace(options.JavaExePath))
            concrete.JavaExePath = options.JavaExePath;
    }

    public static string DefaultArtifactsRoot(string repoRoot) =>
        Path.Combine(repoRoot, "tests", "tools", "artifacts", "npc-gfx-prep");

    public static string ResolveArtworkSwf(string clipsRoot, int gfxId) =>
        Path.GetFullPath(Path.Combine(
            clipsRoot,
            "artworks", "big",
            gfxId.ToString(CultureInfo.InvariantCulture) + ".swf"));

    public static string? ResolveManualPng(string? libraryRoot, int gfxId) =>
        ResolveManualPng(libraryRoot, gfxId, VisualAssetCategory.Mobs);

    public static string? ResolveManualPng(string? libraryRoot, int gfxId, VisualAssetCategory category)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot) || gfxId <= 0)
            return null;
        return Path.Combine(
            libraryRoot,
            PortableVisualStore.VisualsFolderName,
            PortableVisualStore.CategoryFolderName(category),
            gfxId.ToString(CultureInfo.InvariantCulture) + ".png");
    }

    public NpcGfxPreviewPrepEntry ProcessOne(NpcGfxPreviewPrepOptions options, int gfxId)
    {
        ApplyJavaFromOptions(options);
        if (gfxId <= 0)
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "gfxId inválido",
                Zoom = options.Zoom,
            };
        }

        if (string.IsNullOrWhiteSpace(options.FfdecCliPath) || !File.Exists(options.FfdecCliPath))
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "ffdec-cli.exe no encontrado: " + (options.FfdecCliPath ?? "(null)"),
                Zoom = options.Zoom,
            };
        }

        var swf = ResolveArtworkSwf(options.ClipsRoot, gfxId);
        return ProcessSwfFile(options, gfxId, swf, "Sin artworks/big/{gfxId}.swf");
    }

    /// <summary>Export one SWF to staging PNG (FFDec / Svg.Skia FullRootSvg → validate → crop-normalize).</summary>
    public NpcGfxPreviewPrepEntry ProcessSwfFile(
        NpcGfxPreviewPrepOptions options,
        int gfxId,
        string swfPath,
        string? missingSwfReason = null,
        string? stagingOutputBaseName = null,
        bool skipLegacyLibraryLookup = false)
    {
        ApplyJavaFromOptions(options);
        var outputBase = string.IsNullOrWhiteSpace(stagingOutputBaseName)
            ? gfxId.ToString(CultureInfo.InvariantCulture)
            : stagingOutputBaseName.Trim();

        if (gfxId <= 0 && string.IsNullOrWhiteSpace(stagingOutputBaseName))
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "gfxId inválido",
                Zoom = options.Zoom,
            };
        }

        if (string.IsNullOrWhiteSpace(options.FfdecCliPath) || !File.Exists(options.FfdecCliPath))
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "ffdec-cli.exe no encontrado: " + (options.FfdecCliPath ?? "(null)"),
                Zoom = options.Zoom,
            };
        }

        if (!skipLegacyLibraryLookup)
        {
            var manual = ResolveManualPng(options.LibraryRoot, gfxId, options.VisualCategory);
            if (manual is not null && File.Exists(manual))
            {
                return new NpcGfxPreviewPrepEntry
                {
                    GfxId = gfxId,
                    Status = NpcGfxPreviewPrepStatus.ManualExists,
                    Reason = "PNG manual existente — no sobrescribir",
                    OutputPng = manual,
                    Zoom = options.Zoom,
                };
            }
        }

        if (!File.Exists(swfPath))
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                SourceSwf = swfPath,
                Status = NpcGfxPreviewPrepStatus.NoArtwork,
                Reason = missingSwfReason ?? "SWF no encontrado",
                Zoom = options.Zoom,
            };
        }

        var swf = swfPath;
        var zoomFolder = FormatZoomFolder(options.Zoom);
        var gfxDir = Path.Combine(options.StagingRoot, zoomFolder, outputBase);
        var stagedPng = Path.Combine(options.StagingRoot, zoomFolder, outputBase + ".png");

        if (options.SkipExistingValidStaging && File.Exists(stagedPng))
        {
            var rawExisting = Path.Combine(options.StagingRoot, zoomFolder, outputBase + ".ffdec.png");
            var gatePath = File.Exists(rawExisting) ? rawExisting : stagedPng;
            var existing = NpcGfxPngContentValidator.ValidateFile(gatePath);
            if (existing.Decoded && existing.ContentStatus == NpcGfxPreviewPrepStatus.Ok)
            {
                return new NpcGfxPreviewPrepEntry
                {
                    GfxId = gfxId,
                    SourceSwf = swf,
                    Status = NpcGfxPreviewPrepStatus.Ok,
                    Width = NpcGfxPreviewCropNormalizer.CanvasSize,
                    Height = NpcGfxPreviewCropNormalizer.CanvasSize,
                    VisibleWidth = existing.VisibleWidth,
                    VisibleHeight = existing.VisibleHeight,
                    OpaquePixelCount = existing.OpaquePixelCount,
                    OpaqueRatio = existing.OpaqueRatio,
                    FileBytes = new FileInfo(stagedPng).Length,
                    OutputPng = stagedPng,
                    Reason = existing.Reason,
                    Zoom = options.Zoom,
                    ExitCode = 0,
                    SourceWidth = existing.Width,
                    SourceHeight = existing.Height,
                    Normalized = File.Exists(rawExisting),
                };
            }
        }

        Directory.CreateDirectory(gfxDir);

        byte[] swfBytes;
        try
        {
            swfBytes = File.ReadAllBytes(swf);
        }
        catch (Exception ex)
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                SourceSwf = swf,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "No se pudo leer SWF: " + ex.Message,
                Zoom = options.Zoom,
            };
        }

        var target = SwfArtworkExportTargetSelector.Select(swfBytes);

        string? produced;
        int? exitCode;
        string methodNote;
        try
        {
            if (target.Kind == SwfArtworkExportTargetSelector.ExportKind.FullRootSvg)
            {
                var full = TryExportFullRootSvg(options, swf, gfxId, gfxDir, target);
                if (!full.Ok)
                    return full.Entry!;
                produced = full.PngPath;
                exitCode = full.ExitCode;
                methodNote = full.MethodNote ?? FormatExportMethod(target);
            }
            else
            {
                var run = _ffdec.RunExportPng(
                    options.FfdecCliPath,
                    swf,
                    gfxDir,
                    target.Kind,
                    target.SpriteCharacterId,
                    options.Zoom,
                    options.ProcessTimeout);

                if (run.TimedOut)
                {
                    return new NpcGfxPreviewPrepEntry
                    {
                        GfxId = gfxId,
                        SourceSwf = swf,
                        Status = NpcGfxPreviewPrepStatus.Failed,
                        Reason = "TIMEOUT",
                        TimedOut = true,
                        ExitCode = run.ExitCode,
                        Zoom = options.Zoom,
                        ExportMethod = FormatExportMethod(target),
                        ExportSpriteId = target.SpriteCharacterId,
                    };
                }

                if (run.ExitCode != 0)
                {
                    return new NpcGfxPreviewPrepEntry
                    {
                        GfxId = gfxId,
                        SourceSwf = swf,
                        Status = NpcGfxPreviewPrepStatus.Failed,
                        Reason = "FFDec exit " + run.ExitCode,
                        ExitCode = run.ExitCode,
                        Zoom = options.Zoom,
                        ExportMethod = FormatExportMethod(target),
                        ExportSpriteId = target.SpriteCharacterId,
                    };
                }

                produced = FfdecExportLocator.FindFirstPng(gfxDir);
                exitCode = run.ExitCode;
                methodNote = FormatExportMethod(target);
            }
        }
        catch (Exception ex)
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                SourceSwf = swf,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "Export excepción: " + ex.GetType().Name + " — " + ex.Message,
                Zoom = options.Zoom,
                ExportMethod = FormatExportMethod(target),
                ExportSpriteId = target.SpriteCharacterId,
            };
        }

        if (produced is null)
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                SourceSwf = swf,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "Export OK pero sin PNG",
                ExitCode = exitCode,
                Zoom = options.Zoom,
                ExportMethod = methodNote,
                ExportSpriteId = target.SpriteCharacterId,
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(stagedPng)!);
        var rawPng = Path.Combine(options.StagingRoot, zoomFolder, outputBase + ".ffdec.png");
        File.Copy(produced, rawPng, overwrite: true);

        // Classification MUST use raw export metrics — never after upscale/center.
        var validation = NpcGfxPngContentValidator.ValidateFile(rawPng);
        var status = validation.Decoded ? validation.ContentStatus : NpcGfxPreviewPrepStatus.Failed;

        var normalized = false;
        int outW = validation.Width;
        int outH = validation.Height;
        long outBytes = validation.FileBytes;

        if (validation.Decoded && validation.OpaquePixelCount > 0)
        {
            try
            {
                var crop = NpcGfxPreviewCropNormalizer.NormalizeFile(rawPng, stagedPng);
                normalized = crop.HasContent;
                outW = crop.CanvasWidth;
                outH = crop.CanvasHeight;
                outBytes = crop.PngBytes.LongLength;
            }
            catch (Exception ex)
            {
                File.Copy(rawPng, stagedPng, overwrite: true);
                return new NpcGfxPreviewPrepEntry
                {
                    GfxId = gfxId,
                    SourceSwf = swf,
                    Status = status,
                    Width = validation.Width,
                    Height = validation.Height,
                    VisibleWidth = validation.VisibleWidth,
                    VisibleHeight = validation.VisibleHeight,
                    OpaquePixelCount = validation.OpaquePixelCount,
                    OpaqueRatio = validation.OpaqueRatio,
                    FileBytes = validation.FileBytes,
                    OutputPng = stagedPng,
                    Reason = ComposeReason(validation.Reason, status, target) + " · normalize fail: " + ex.GetType().Name,
                    Zoom = options.Zoom,
                    ExitCode = exitCode,
                    SourceWidth = validation.Width,
                    SourceHeight = validation.Height,
                    Normalized = false,
                    ExportMethod = methodNote,
                    ExportSpriteId = target.SpriteCharacterId,
                };
            }
        }
        else
        {
            File.Copy(rawPng, stagedPng, overwrite: true);
        }

        var entry = new NpcGfxPreviewPrepEntry
        {
            GfxId = gfxId,
            SourceSwf = swf,
            Status = status,
            Width = outW,
            Height = outH,
            VisibleWidth = validation.VisibleWidth,
            VisibleHeight = validation.VisibleHeight,
            OpaquePixelCount = validation.OpaquePixelCount,
            OpaqueRatio = validation.OpaqueRatio,
            FileBytes = outBytes,
            OutputPng = stagedPng,
            Reason = ComposeReason(validation.Reason, status, target),
            Zoom = options.Zoom,
            ExitCode = exitCode,
            TimedOut = false,
            SourceWidth = validation.Width,
            SourceHeight = validation.Height,
            Normalized = normalized,
            ExportMethod = methodNote,
            ExportSpriteId = target.SpriteCharacterId,
        };

        if (options.PromoteOkToLibrary
            && entry.Status == NpcGfxPreviewPrepStatus.Ok
            && !string.IsNullOrWhiteSpace(options.LibraryRoot))
        {
            PromoteOk(options.LibraryRoot!, gfxId, stagedPng, options.VisualCategory);
        }

        return entry;
    }

    private sealed class FullRootExportOutcome
    {
        public bool Ok { get; init; }
        public string? PngPath { get; init; }
        public int? ExitCode { get; init; }
        public string? MethodNote { get; init; }
        public NpcGfxPreviewPrepEntry? Entry { get; init; }
    }

    private FullRootExportOutcome TryExportFullRootSvg(
        NpcGfxPreviewPrepOptions options,
        string swf,
        int gfxId,
        string gfxDir,
        SwfArtworkExportTargetSelector.Selection target)
    {
        var svgDir = Path.Combine(gfxDir, "svg");
        Directory.CreateDirectory(svgDir);
        var svgRun = _ffdec.RunExportFrameSvg(
            options.FfdecCliPath, swf, svgDir, options.ProcessTimeout);

        if (svgRun.TimedOut)
        {
            return FailFullRoot(gfxId, swf, options, target, timedOut: true, exit: svgRun.ExitCode,
                reason: "TIMEOUT SVG");
        }

        if (svgRun.ExitCode != 0)
        {
            return FailFullRoot(gfxId, swf, options, target, timedOut: false, exit: svgRun.ExitCode,
                reason: "FFDec SVG exit " + svgRun.ExitCode);
        }

        var svgPath = FfdecExportLocator.FindFirstSvg(svgDir);
        if (svgPath is null)
        {
            return FailFullRoot(gfxId, swf, options, target, timedOut: false, exit: svgRun.ExitCode,
                reason: "FFDec SVG OK pero sin .svg");
        }

        // Expand viewBox for ContentBounds (user-space AABB). Svg.Skia paints original SVG
        // with Stage ∪ ContentBounds ∪ CullRect (+ margin / edge-touch).
        var expandedPath = Path.Combine(svgDir, "expanded.svg");
        var expand = SvgFrameBoundsExpander.ExpandFile(svgPath, expandedPath);
        SvgFrameBoundsExpander.Bounds? content =
            expand.Ok && expand.ContentBounds.IsValid ? expand.ContentBounds : null;

        var pngPath = Path.Combine(gfxDir, "full-root.png");
        var raster = SvgSkiaFrameRasterizer.RasterizeToPng(
            originalSvgPath: svgPath,
            outputPngPath: pngPath,
            contentBounds: content,
            margin: SvgSkiaFrameRasterizer.DefaultMargin,
            expandedSvgFallbackPath: expand.Ok ? expandedPath : null);

        if (!raster.Ok || string.IsNullOrWhiteSpace(raster.PngPath) || !File.Exists(raster.PngPath))
        {
            return FailFullRoot(gfxId, swf, options, target, timedOut: false, exit: -1,
                reason: "Svg.Skia no produjo PNG: " + (raster.Error ?? "(sin detalle)"));
        }

        return new FullRootExportOutcome
        {
            Ok = true,
            PngPath = raster.PngPath,
            ExitCode = 0,
            MethodNote = "full-root-svg",
        };
    }

    private static FullRootExportOutcome FailFullRoot(
        int gfxId,
        string swf,
        NpcGfxPreviewPrepOptions options,
        SwfArtworkExportTargetSelector.Selection target,
        bool timedOut,
        int exit,
        string reason) =>
        new()
        {
            Ok = false,
            Entry = new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                SourceSwf = swf,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = reason,
                TimedOut = timedOut,
                ExitCode = exit,
                Zoom = options.Zoom,
                ExportMethod = FormatExportMethod(target),
                ExportSpriteId = target.SpriteCharacterId,
            },
        };

    public NpcGfxPreviewPrepSummary ProcessMany(NpcGfxPreviewPrepOptions options)
    {
        Directory.CreateDirectory(options.StagingRoot);
        var entries = new List<NpcGfxPreviewPrepEntry>(options.GfxIds.Count);
        foreach (var id in options.GfxIds.Distinct().OrderBy(x => x))
            entries.Add(ProcessOne(options, id));

        return BuildSummary(entries, options.StagingRoot, confirmedGfxCount: options.GfxIds.Distinct().Count());
    }

    public static NpcGfxPreviewPrepSummary BuildSummary(
        IReadOnlyList<NpcGfxPreviewPrepEntry> entries,
        string stagingRoot,
        int confirmedGfxCount)
    {
        var withArt = entries.Count(e => e.Status != NpcGfxPreviewPrepStatus.NoArtwork);
        // with artwork among processed that had opportunity — count by status
        var noArt = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.NoArtwork);
        return new NpcGfxPreviewPrepSummary
        {
            ConfirmedGfxCount = confirmedGfxCount,
            WithArtwork = entries.Count - noArt,
            WithoutArtwork = noArt,
            Processed = entries.Count,
            Ok = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.Ok),
            Review = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.Review),
            Failed = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.Failed),
            NoArtwork = noArt,
            ManualExists = entries.Count(e => e.Status == NpcGfxPreviewPrepStatus.ManualExists),
            Entries = entries,
            StagingRoot = stagingRoot,
        };
    }

    public static void WriteManifest(NpcGfxPreviewPrepSummary summary, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dto = new
        {
            summary.ConfirmedGfxCount,
            summary.WithArtwork,
            summary.WithoutArtwork,
            summary.Processed,
            summary.Ok,
            summary.Review,
            summary.Failed,
            summary.NoArtwork,
            summary.ManualExists,
            summary.StagingRoot,
            summary.RecommendedZoomNote,
            Entries = summary.Entries.Select(e => new
            {
                e.GfxId,
                SourceSwf = RelativizeForManifest(e.SourceSwf),
                Status = e.Status.ToString().ToUpperInvariant(),
                e.Width,
                e.Height,
                VisibleBounds = e.VisibleWidth > 0 ? $"{e.VisibleWidth}x{e.VisibleHeight}" : "",
                SourceFrame = e.SourceWidth > 0 ? $"{e.SourceWidth}x{e.SourceHeight}" : "",
                e.OpaquePixelCount,
                OpaqueRatio = Math.Round(e.OpaqueRatio, 5),
                e.FileBytes,
                OutputPng = RelativizeForManifest(e.OutputPng),
                e.Reason,
                e.Zoom,
                e.ExitCode,
                e.TimedOut,
                e.Normalized,
                e.ExportMethod,
                e.ExportSpriteId,
            }),
        };

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static void WriteIndexHtml(NpcGfxPreviewPrepSummary summary, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<title>Npc GFX prep sample</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#1b1b1b;color:#eee;padding:16px}");
        sb.AppendLine(".grid{display:flex;flex-wrap:wrap;gap:12px}");
        sb.AppendLine(".card{width:200px;background:#2a2a2a;padding:8px;border-radius:6px}");
        sb.AppendLine("img{width:160px;height:160px;object-fit:contain;background:#333;image-rendering:auto}");
        sb.AppendLine(".meta{font-size:11px;color:#bbb;margin-top:4px}");
        sb.AppendLine(".OK{color:#8f8}.REVIEW{color:#fd8}.FAILED{color:#f88}.NO_ARTWORK,.MANUAL_EXISTS{color:#aaa}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"<h1>PrepareNpcGfxPreviews · normalized {NpcGfxPreviewCropNormalizer.CanvasSize}px</h1>");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"<p>OK={summary.Ok} REVIEW={summary.Review} FAILED={summary.Failed} NO_ARTWORK={summary.NoArtwork} MANUAL={summary.ManualExists}</p>");
        sb.AppendLine("<div class=\"grid\">");
        foreach (var e in summary.Entries
                     .OrderBy(x => x.Status switch
                     {
                         NpcGfxPreviewPrepStatus.Failed => 0,
                         NpcGfxPreviewPrepStatus.Review => 1,
                         NpcGfxPreviewPrepStatus.Ok => 2,
                         NpcGfxPreviewPrepStatus.ManualExists => 3,
                         _ => 4,
                     })
                     .ThenBy(x => x.GfxId))
        {
            var status = e.Status.ToString().ToUpperInvariant();
            sb.Append("<div class=\"card\"><div class=\"").Append(status).Append("\">#")
                .Append(e.GfxId).Append(' ').Append(status).Append("</div>");
            if (!string.IsNullOrWhiteSpace(e.OutputPng) && File.Exists(e.OutputPng)
                && e.Status is not NpcGfxPreviewPrepStatus.NoArtwork
                and not NpcGfxPreviewPrepStatus.ManualExists)
            {
                var rel = Path.GetRelativePath(Path.GetDirectoryName(path)!, e.OutputPng!).Replace('\\', '/');
                sb.Append("<img src=\"").Append(rel).Append("\" alt=\"").Append(e.GfxId).Append("\"/>");
            }

            sb.Append("<div class=\"meta\">opaque=").Append(e.OpaquePixelCount)
                .Append(" · vis=").Append(e.VisibleWidth).Append('x').Append(e.VisibleHeight);
            if (e.SourceWidth > 0)
                sb.Append(" · src=").Append(e.SourceWidth).Append('x').Append(e.SourceHeight);
            if (e.Normalized)
                sb.Append(" · out=").Append(e.Width).Append('x').Append(e.Height);
            if (!string.IsNullOrWhiteSpace(e.ExportMethod))
            {
                var methodLabel = e.ExportMethod.StartsWith("sprite:", StringComparison.OrdinalIgnoreCase)
                    ? "sprite"
                    : e.ExportMethod;
                sb.Append(" · method=").Append(System.Net.WebUtility.HtmlEncode(methodLabel));
                if (!string.Equals(methodLabel, e.ExportMethod, StringComparison.Ordinal))
                    sb.Append(" (").Append(System.Net.WebUtility.HtmlEncode(e.ExportMethod)).Append(')');
            }
            sb.Append("</div>");
            sb.Append("<div class=\"meta\">").Append(System.Net.WebUtility.HtmlEncode(e.Reason ?? "")).Append("</div></div>");
        }

        sb.AppendLine("</div></body></html>");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void PromoteOk(
        string libraryRoot,
        int gfxId,
        string stagedPng,
        VisualAssetCategory category)
    {
        var dest = ResolveManualPng(libraryRoot, gfxId, category);
        if (dest is null)
            return;
        if (File.Exists(dest))
            return; // manual / prior — never overwrite

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        // Staging already crop-normalized to MaxPreviewEdge — copy as-is (no VisualImageNormalizer).
        File.Copy(stagedPng, dest, overwrite: false);
    }

    /// <summary>
    /// ADMIN.UI.4B.2A.3G.5 — promote OK PNGs from an existing batch staging folder.
    /// Reads <c>manifest-native.json</c>; copies only Status=OK; never overwrites Library files.
    /// Does not invoke FFDec/Inkscape.
    /// </summary>
    public static NpcGfxPromoteFromBatchResult PromoteOkFromBatch(
        string batchRoot,
        string libraryRoot,
        string? reportPath = null)
    {
        if (string.IsNullOrWhiteSpace(batchRoot) || !Directory.Exists(batchRoot))
            throw new DirectoryNotFoundException("Batch staging no encontrado: " + batchRoot);
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("Library root requerido.", nameof(libraryRoot));

        var manifestPath = Path.Combine(batchRoot, "manifest-native.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("manifest-native.json ausente en batch.", manifestPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!doc.RootElement.TryGetProperty("Entries", out var entriesEl)
            || entriesEl.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Manifest sin Entries[].");

        var created = new List<int>();
        var skippedManual = new List<int>();
        var skippedStatus = new List<(int GfxId, string Status)>();
        var skippedMissingPng = new List<int>();
        var okDetected = 0;

        foreach (var e in entriesEl.EnumerateArray())
        {
            if (!e.TryGetProperty("GfxId", out var idEl) || !idEl.TryGetInt32(out var gfxId) || gfxId <= 0)
                continue;

            var status = e.TryGetProperty("Status", out var stEl) && stEl.ValueKind == JsonValueKind.String
                ? stEl.GetString() ?? ""
                : "";

            // Manifest writes ToUpperInvariant without underscores for some enums historically,
            // but OK is always "OK".
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                skippedStatus.Add((gfxId, status));
                continue;
            }

            okDetected++;
            var staged = Path.Combine(
                batchRoot,
                FormatZoomFolder(1),
                gfxId.ToString(CultureInfo.InvariantCulture) + ".png");
            if (!File.Exists(staged))
            {
                // Fallback: OutputPng relative hint in manifest (e.g. native/30.png)
                if (e.TryGetProperty("OutputPng", out var outEl)
                    && outEl.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(outEl.GetString()))
                {
                    var rel = outEl.GetString()!.Replace('/', Path.DirectorySeparatorChar);
                    var candidate = Path.IsPathRooted(rel) ? rel : Path.Combine(batchRoot, rel);
                    if (File.Exists(candidate))
                        staged = candidate;
                }
            }

            if (!File.Exists(staged))
            {
                skippedMissingPng.Add(gfxId);
                continue;
            }

            var dest = ResolveManualPng(libraryRoot, gfxId);
            if (dest is null)
            {
                skippedMissingPng.Add(gfxId);
                continue;
            }

            if (File.Exists(dest))
            {
                skippedManual.Add(gfxId);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(staged, dest, overwrite: false);
            if (File.Exists(dest))
                created.Add(gfxId);
        }

        var result = new NpcGfxPromoteFromBatchResult
        {
            BatchRoot = Path.GetFullPath(batchRoot),
            LibraryRoot = Path.GetFullPath(libraryRoot),
            OkDetected = okDetected,
            Created = created,
            SkippedManual = skippedManual,
            SkippedStatus = skippedStatus,
            SkippedMissingPng = skippedMissingPng,
            ManualOverwrites = 0,
        };

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            var report = new
            {
                result.BatchRoot,
                result.LibraryRoot,
                result.OkDetected,
                CreatedCount = result.Created.Count,
                result.Created,
                SkippedManualCount = result.SkippedManual.Count,
                result.SkippedManual,
                SkippedStatusCount = result.SkippedStatus.Count,
                SkippedStatus = result.SkippedStatus.Select(x => new { x.GfxId, x.Status }),
                SkippedMissingPngCount = result.SkippedMissingPng.Count,
                result.SkippedMissingPng,
                result.ManualOverwrites,
            };
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                }),
                Encoding.UTF8);
        }

        return result;
    }

    public static string FormatZoomFolder(double zoom) =>
        zoom > 1.0001
            ? "zoom" + zoom.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_')
            : "native";

    private static string FormatExportMethod(SwfArtworkExportTargetSelector.Selection target) =>
        target.Kind switch
        {
            SwfArtworkExportTargetSelector.ExportKind.Sprite =>
                "sprite:" + target.SpriteCharacterId,
            SwfArtworkExportTargetSelector.ExportKind.FullRootSvg =>
                "full-root-svg",
            _ => "frame",
        };

    private static string ComposeReason(
        string? validationReason,
        NpcGfxPreviewPrepStatus status,
        SwfArtworkExportTargetSelector.Selection target)
    {
        var baseReason = validationReason ?? status.ToString();
        return baseReason + " · " + target.Reason;
    }

    private static string? RelativizeForManifest(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        // Prefer basename + parent folder only — avoid leaking full user profiles into shared logs.
        try
        {
            var file = Path.GetFileName(path);
            var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            return string.IsNullOrEmpty(parent) ? file : parent + "/" + file;
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }
}
