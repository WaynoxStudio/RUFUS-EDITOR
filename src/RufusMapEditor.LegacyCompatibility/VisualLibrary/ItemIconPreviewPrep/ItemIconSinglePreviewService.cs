using System.Globalization;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.RuntimeTools;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>
/// CONTENT.ITEMS.SVG_RASTERIZER.INTEGRATE.1 / RUNTIME_TOOLS.INTEGRATE.1 —
/// generate / promote one item SWF preview via the real pipeline.
/// Not wired to ItemPickerWindow.
/// </summary>
public sealed class ItemIconSinglePreviewService
{
    private readonly NpcGfxPreviewPrepService _core;

    public ItemIconSinglePreviewService(NpcGfxPreviewPrepService? core = null)
    {
        _core = core ?? new NpcGfxPreviewPrepService();
    }

    /// <summary>
    /// Resolve tools-cache (lazy FFDec extract) then generate one preview.
    /// Soft-fails with ToolsUnavailable / FfdecUnavailable — never throws for missing tools.
    /// </summary>
    public ItemIconSinglePreviewResult GenerateOneWithRuntimeTools(
        string clipsRoot,
        string stagingRoot,
        string previewKeyOrSwfRelativePath,
        RuntimeToolsResolver? tools = null,
        string? libraryRoot = null,
        TimeSpan? processTimeout = null,
        double zoom = 1)
    {
        tools ??= new RuntimeToolsResolver();
        var resolved = tools.EnsureReady();
        if (!resolved.CanRunFfdec)
        {
            return new ItemIconSinglePreviewResult
            {
                PreviewKey = previewKeyOrSwfRelativePath?.Trim() ?? "",
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = (resolved.Availability + ": " + (resolved.Reason ?? "herramientas no disponibles")).Trim(),
                ToolsAvailability = resolved.Availability,
                JavaManaged = resolved.JavaManaged,
            };
        }

        var runner = new FfdecCliProcessRunner(resolved.JavaHome, resolved.JavaExePath);
        var core = new NpcGfxPreviewPrepService(runner);
        var svc = new ItemIconSinglePreviewService(core);
        var result = svc.GenerateOne(
            resolved.FfdecCliPath!,
            clipsRoot,
            stagingRoot,
            previewKeyOrSwfRelativePath,
            libraryRoot,
            processTimeout,
            zoom,
            resolved.JavaHome,
            resolved.JavaExePath);

        return new ItemIconSinglePreviewResult
        {
            PreviewKey = result.PreviewKey,
            PreviewStem = result.PreviewStem,
            GfxId = result.GfxId,
            TypeId = result.TypeId,
            ResolvedSwfRelativePath = result.ResolvedSwfRelativePath,
            SourceSwf = result.SourceSwf,
            Status = result.Status,
            Reason = result.Status == NpcGfxPreviewPrepStatus.Failed
                     && string.IsNullOrWhiteSpace(result.Reason)
                ? "FfdecFailed"
                : result.Reason,
            OutputPng = result.OutputPng,
            ExportMethod = result.ExportMethod,
            ExportSpriteId = result.ExportSpriteId,
            Width = result.Width,
            Height = result.Height,
            FileBytes = result.FileBytes,
            StagingRoot = result.StagingRoot,
            ToolsAvailability = resolved.Availability,
            JavaManaged = resolved.JavaManaged,
            FfdecCliPath = resolved.FfdecCliPath,
        };
    }

    public ItemIconSinglePreviewResult GenerateOne(
        string ffdecCliPath,
        string clipsRoot,
        string stagingRoot,
        string previewKeyOrSwfRelativePath,
        string? libraryRoot = null,
        TimeSpan? processTimeout = null,
        double zoom = 1,
        string? javaHome = null,
        string? javaExePath = null)
    {
        if (!ItemIconPreviewKey.TryFromResolvedSwfRelativePath(previewKeyOrSwfRelativePath, out var key))
        {
            return new ItemIconSinglePreviewResult
            {
                PreviewKey = previewKeyOrSwfRelativePath?.Trim() ?? "",
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "PreviewKey inválido (se esperaba items/{folder}/{gfx}.swf)",
            };
        }

        if (string.IsNullOrWhiteSpace(ffdecCliPath) || !File.Exists(ffdecCliPath))
        {
            return new ItemIconSinglePreviewResult
            {
                PreviewKey = key.Key,
                PreviewStem = key.FileStem,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "ToolsUnavailable — ffdec-cli.exe no encontrado: " + (ffdecCliPath ?? "(null)"),
                ToolsAvailability = RuntimeToolsAvailability.FfdecUnavailable,
            };
        }

        if (string.IsNullOrWhiteSpace(clipsRoot) || !Directory.Exists(clipsRoot))
        {
            return new ItemIconSinglePreviewResult
            {
                PreviewKey = key.Key,
                PreviewStem = key.FileStem,
                Status = NpcGfxPreviewPrepStatus.Failed,
                Reason = "ClipsRoot inexistente: " + (clipsRoot ?? "(null)"),
            };
        }

        var parts = key.ResolvedSwfRelativePath.Split('/');
        var typeId = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var gfxId = int.Parse(parts[2].AsSpan(0, parts[2].Length - 4), CultureInfo.InvariantCulture);

        var full = Path.GetFullPath(Path.Combine(
            clipsRoot.Trim(),
            key.ResolvedSwfRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!File.Exists(full))
        {
            return new ItemIconSinglePreviewResult
            {
                PreviewKey = key.Key,
                PreviewStem = key.FileStem,
                GfxId = gfxId,
                TypeId = typeId,
                Status = NpcGfxPreviewPrepStatus.NoArtwork,
                Reason = "NO_ICON — SWF no encontrado: " + key.ResolvedSwfRelativePath,
                ResolvedSwfRelativePath = key.ResolvedSwfRelativePath,
            };
        }

        Directory.CreateDirectory(stagingRoot);
        var opts = new NpcGfxPreviewPrepOptions
        {
            FfdecCliPath = ffdecCliPath,
            ClipsRoot = clipsRoot,
            StagingRoot = stagingRoot,
            LibraryRoot = libraryRoot,
            Zoom = zoom,
            ProcessTimeout = processTimeout ?? TimeSpan.FromSeconds(45),
            PromoteOkToLibrary = false,
            VisualCategory = VisualAssetCategory.Items,
            SkipExistingValidStaging = false,
            JavaHome = javaHome,
            JavaExePath = javaExePath,
        };

        var core = _core.ProcessSwfFile(
            opts,
            gfxId,
            full,
            $"NO_ICON — sin {key.ResolvedSwfRelativePath}",
            stagingOutputBaseName: key.FileStem,
            skipLegacyLibraryLookup: true);

        return new ItemIconSinglePreviewResult
        {
            PreviewKey = key.Key,
            PreviewStem = key.FileStem,
            GfxId = gfxId,
            TypeId = typeId,
            ResolvedSwfRelativePath = key.ResolvedSwfRelativePath,
            SourceSwf = core.SourceSwf ?? full,
            Status = core.Status,
            Reason = core.Reason,
            OutputPng = core.OutputPng,
            ExportMethod = core.ExportMethod,
            ExportSpriteId = core.ExportSpriteId,
            Width = core.Width,
            Height = core.Height,
            FileBytes = core.FileBytes,
            StagingRoot = stagingRoot,
            FfdecCliPath = ffdecCliPath,
        };
    }

    /// <summary>
    /// Promote one staging PNG to ItemsBySwf under <paramref name="libraryRoot"/>
    /// (use a temp library for tests — does not require batch manifest).
    /// </summary>
    public static ItemIconSinglePromoteResult PromoteOne(
        ItemIconSinglePreviewResult generated,
        string libraryRoot,
        bool overwrite = false,
        bool includeReview = true)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("Library root requerido.", nameof(libraryRoot));

        if (!ItemIconPreviewKey.TryFromResolvedSwfRelativePath(generated.PreviewKey, out var key))
        {
            return new ItemIconSinglePromoteResult
            {
                PreviewKey = generated.PreviewKey,
                Ok = false,
                Reason = "PreviewKey inválido",
            };
        }

        var promotable = ItemIconPreviewPrepService.IsPromotableStatus(generated.Status)
            || (includeReview && generated.Status == NpcGfxPreviewPrepStatus.Review);
        if (!promotable)
        {
            return new ItemIconSinglePromoteResult
            {
                PreviewKey = key.Key,
                Ok = false,
                Reason = "Status no promocionable: " + generated.Status,
                Status = generated.Status.ToString().ToUpperInvariant(),
            };
        }

        var staged = generated.OutputPng;
        if (string.IsNullOrWhiteSpace(staged) || !File.Exists(staged))
        {
            return new ItemIconSinglePromoteResult
            {
                PreviewKey = key.Key,
                Ok = false,
                Reason = "PNG staging ausente",
                Status = generated.Status.ToString().ToUpperInvariant(),
            };
        }

        var destDir = Path.Combine(
            libraryRoot,
            PortableVisualStore.VisualsFolderName,
            PortableVisualStore.ItemsBySwfFolderName);
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, key.StagingPngFileName);

        if (File.Exists(dest) && !overwrite)
        {
            var existing = NpcGfxPngContentValidator.ValidateFile(dest);
            if (existing.Decoded && ItemIconPreviewPrepService.IsPromotableStatus(existing.ContentStatus))
            {
                return new ItemIconSinglePromoteResult
                {
                    PreviewKey = key.Key,
                    Ok = true,
                    SkippedExisting = true,
                    DestinationPng = dest,
                    Status = generated.Status.ToString().ToUpperInvariant(),
                    Reason = "PNG destino ya existe — no sobrescrito",
                };
            }
        }

        File.Copy(staged, dest, overwrite: overwrite || !File.Exists(dest));
        return new ItemIconSinglePromoteResult
        {
            PreviewKey = key.Key,
            Ok = true,
            DestinationPng = dest,
            Status = generated.Status.ToString().ToUpperInvariant(),
            Reason = "Promovido",
        };
    }
}

public sealed class ItemIconSinglePreviewResult
{
    public required string PreviewKey { get; init; }
    public string? PreviewStem { get; init; }
    public int GfxId { get; init; }
    public int TypeId { get; init; }
    public string? ResolvedSwfRelativePath { get; init; }
    public string? SourceSwf { get; init; }
    public required NpcGfxPreviewPrepStatus Status { get; init; }
    public string? Reason { get; init; }
    public string? OutputPng { get; init; }
    public string? ExportMethod { get; init; }
    public int? ExportSpriteId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long FileBytes { get; init; }
    public string? StagingRoot { get; init; }
    public RuntimeToolsAvailability? ToolsAvailability { get; init; }
    public bool JavaManaged { get; init; }
    public string? FfdecCliPath { get; init; }
}

public sealed class ItemIconSinglePromoteResult
{
    public required string PreviewKey { get; init; }
    public required bool Ok { get; init; }
    public string? DestinationPng { get; init; }
    public bool SkippedExisting { get; init; }
    public string? Status { get; init; }
    public string? Reason { get; init; }
}
