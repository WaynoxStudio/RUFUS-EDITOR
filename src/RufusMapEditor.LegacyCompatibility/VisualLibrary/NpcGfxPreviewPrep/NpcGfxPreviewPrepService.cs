using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>ADMIN.UI.4B.2A.3G.1 — orchestrates FFDec export → validate → staging (optional promote).</summary>
public sealed class NpcGfxPreviewPrepService
{
    private readonly IFfdecProcessRunner _ffdec;

    public NpcGfxPreviewPrepService(IFfdecProcessRunner? ffdec = null) =>
        _ffdec = ffdec ?? new FfdecCliProcessRunner();

    public static string DefaultArtifactsRoot(string repoRoot) =>
        Path.Combine(repoRoot, "tests", "tools", "artifacts", "npc-gfx-prep");

    public static string ResolveArtworkSwf(string clipsRoot, int gfxId) =>
        Path.GetFullPath(Path.Combine(
            clipsRoot,
            "artworks", "big",
            gfxId.ToString(CultureInfo.InvariantCulture) + ".swf"));

    public static string? ResolveManualPng(string? libraryRoot, int gfxId)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot) || gfxId <= 0)
            return null;
        return Path.Combine(
            libraryRoot,
            PortableVisualStore.VisualsFolderName,
            PortableVisualStore.MobsFolderName,
            gfxId.ToString(CultureInfo.InvariantCulture) + ".png");
    }

    public NpcGfxPreviewPrepEntry ProcessOne(NpcGfxPreviewPrepOptions options, int gfxId)
    {
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

        var manual = ResolveManualPng(options.LibraryRoot, gfxId);
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

        var swf = ResolveArtworkSwf(options.ClipsRoot, gfxId);
        if (!File.Exists(swf))
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                SourceSwf = swf,
                Status = NpcGfxPreviewPrepStatus.NoArtwork,
                Reason = "Sin artworks/big/{gfxId}.swf",
                Zoom = options.Zoom,
            };
        }

        var zoomFolder = FormatZoomFolder(options.Zoom);
        var gfxDir = Path.Combine(options.StagingRoot, zoomFolder, gfxId.ToString(CultureInfo.InvariantCulture));
        var stagedPng = Path.Combine(options.StagingRoot, zoomFolder, gfxId.ToString(CultureInfo.InvariantCulture) + ".png");

        if (options.SkipExistingValidStaging && File.Exists(stagedPng))
        {
            var existing = NpcGfxPngContentValidator.ValidateFile(stagedPng);
            if (existing.Decoded && existing.ContentStatus == NpcGfxPreviewPrepStatus.Ok)
            {
                return ToEntry(gfxId, swf, stagedPng, existing, options.Zoom, exitCode: 0, timedOut: false,
                    existing.Reason);
            }
        }

        Directory.CreateDirectory(gfxDir);
        FfdecRunResult run;
        try
        {
            run = _ffdec.RunExportFramePng(
                options.FfdecCliPath,
                swf,
                gfxDir,
                options.Zoom,
                options.ProcessTimeout);
        }
        catch (Exception ex)
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                SourceSwf = swf,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "FFDec excepción: " + ex.GetType().Name + " — " + ex.Message,
                Zoom = options.Zoom,
            };
        }

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
            };
        }

        var produced = FfdecExportLocator.FindFirstPng(gfxDir);
        if (produced is null)
        {
            return new NpcGfxPreviewPrepEntry
            {
                GfxId = gfxId,
                SourceSwf = swf,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "FFDec OK pero sin PNG",
                ExitCode = run.ExitCode,
                Zoom = options.Zoom,
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(stagedPng)!);
        File.Copy(produced, stagedPng, overwrite: true);

        var validation = NpcGfxPngContentValidator.ValidateFile(stagedPng);
        var entry = ToEntry(gfxId, swf, stagedPng, validation, options.Zoom, run.ExitCode, false, validation.Reason);

        if (options.PromoteOkToLibrary
            && entry.Status == NpcGfxPreviewPrepStatus.Ok
            && !string.IsNullOrWhiteSpace(options.LibraryRoot))
        {
            PromoteOk(options.LibraryRoot!, gfxId, stagedPng, entry);
        }

        return entry;
    }

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
                e.OpaquePixelCount,
                OpaqueRatio = Math.Round(e.OpaqueRatio, 5),
                e.FileBytes,
                OutputPng = RelativizeForManifest(e.OutputPng),
                e.Reason,
                e.Zoom,
                e.ExitCode,
                e.TimedOut,
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
        sb.AppendLine(".card{width:180px;background:#2a2a2a;padding:8px;border-radius:6px}");
        sb.AppendLine("img{max-width:160px;max-height:160px;background:#444}");
        sb.AppendLine(".OK{color:#8f8}.REVIEW{color:#fd8}.FAILED{color:#f88}.NO_ARTWORK,.MANUAL_EXISTS{color:#aaa}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"<h1>PrepareNpcGfxPreviews sample</h1><p>OK={summary.Ok} REVIEW={summary.Review} FAILED={summary.Failed} NO_ARTWORK={summary.NoArtwork} MANUAL={summary.ManualExists}</p>");
        sb.AppendLine("<div class=\"grid\">");
        foreach (var e in summary.Entries)
        {
            var status = e.Status.ToString().ToUpperInvariant();
            sb.Append("<div class=\"card\"><div class=\"").Append(status).Append("\">#")
                .Append(e.GfxId).Append(' ').Append(status).Append("</div>");
            if (!string.IsNullOrWhiteSpace(e.OutputPng) && File.Exists(e.OutputPng)
                && e.Status is NpcGfxPreviewPrepStatus.Ok or NpcGfxPreviewPrepStatus.Review)
            {
                var rel = Path.GetRelativePath(Path.GetDirectoryName(path)!, e.OutputPng!).Replace('\\', '/');
                sb.Append("<img src=\"").Append(rel).Append("\" alt=\"").Append(e.GfxId).Append("\"/>");
            }

            sb.Append("<div>").Append(System.Net.WebUtility.HtmlEncode(e.Reason ?? "")).Append("</div></div>");
        }

        sb.AppendLine("</div></body></html>");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void PromoteOk(string libraryRoot, int gfxId, string stagedPng, NpcGfxPreviewPrepEntry entry)
    {
        var dest = ResolveManualPng(libraryRoot, gfxId);
        if (dest is null)
            return;
        if (File.Exists(dest))
            return; // manual / prior — never overwrite

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        // Copy master as-is — do NOT run VisualImageNormalizer (would shrink to 256).
        File.Copy(stagedPng, dest, overwrite: false);
    }

    private static NpcGfxPreviewPrepEntry ToEntry(
        int gfxId,
        string swf,
        string png,
        NpcGfxPngValidationResult v,
        double zoom,
        int? exitCode,
        bool timedOut,
        string? reason) =>
        new()
        {
            GfxId = gfxId,
            SourceSwf = swf,
            Status = v.Decoded ? v.ContentStatus : NpcGfxPreviewPrepStatus.Failed,
            Width = v.Width,
            Height = v.Height,
            VisibleWidth = v.VisibleWidth,
            VisibleHeight = v.VisibleHeight,
            OpaquePixelCount = v.OpaquePixelCount,
            OpaqueRatio = v.OpaqueRatio,
            FileBytes = v.FileBytes,
            OutputPng = png,
            Reason = reason ?? v.Reason,
            Zoom = zoom,
            ExitCode = exitCode,
            TimedOut = timedOut,
        };

    public static string FormatZoomFolder(double zoom) =>
        zoom > 1.0001
            ? "zoom" + zoom.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_')
            : "native";

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
