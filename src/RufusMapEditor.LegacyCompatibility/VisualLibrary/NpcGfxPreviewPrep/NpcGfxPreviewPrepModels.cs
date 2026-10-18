namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>ADMIN.UI.4B.2A.3G.1 — one row in the prep manifest.</summary>
public sealed class NpcGfxPreviewPrepEntry
{
    public required int GfxId { get; init; }
    public string? SourceSwf { get; init; }
    public required NpcGfxPreviewPrepStatus Status { get; init; }
    /// <summary>Output (normalized) canvas size.</summary>
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>Visible bounds on the raw FFDec frame (pre-normalize). Classification uses these.</summary>
    public int VisibleWidth { get; init; }
    public int VisibleHeight { get; init; }
    public int OpaquePixelCount { get; init; }
    public double OpaqueRatio { get; init; }
    public long FileBytes { get; init; }
    public string? OutputPng { get; init; }
    public string? Reason { get; init; }
    public double Zoom { get; init; } = 1;
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
    /// <summary>Raw FFDec frame size before crop/center.</summary>
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public bool Normalized { get; init; }
    public string? ExportMethod { get; init; }
    public int? ExportSpriteId { get; init; }
}

public sealed class NpcGfxPreviewPrepSummary
{
    public required int ConfirmedGfxCount { get; init; }
    public required int WithArtwork { get; init; }
    public required int WithoutArtwork { get; init; }
    public required int Processed { get; init; }
    public required int Ok { get; init; }
    public required int Review { get; init; }
    public required int Failed { get; init; }
    public required int NoArtwork { get; init; }
    public required int ManualExists { get; init; }
    public required IReadOnlyList<NpcGfxPreviewPrepEntry> Entries { get; init; }
    public required string StagingRoot { get; init; }
    public string? RecommendedZoomNote { get; init; }
}

public sealed class NpcGfxPreviewPrepOptions
{
    public required string FfdecCliPath { get; init; }
    public required string ClipsRoot { get; init; }
    public required string StagingRoot { get; init; }
    /// <summary>Optional Library root — used to detect manuals and optional promote.</summary>
    public string? LibraryRoot { get; init; }
    public IReadOnlyList<int> GfxIds { get; init; } = Array.Empty<int>();
    public double Zoom { get; init; } = 1;
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public bool PromoteOkToLibrary { get; init; }
    /// <summary>When true, skip FFDec if staging PNG for this zoom already exists and validates OK.</summary>
    public bool SkipExistingValidStaging { get; init; }
    /// <summary>
    /// Optional legacy Inkscape path (unused by FullRootSvg after CONTENT.ITEMS.SVG_RASTERIZER.INTEGRATE.1).
    /// Kept for API compatibility with existing prep tools/options.
    /// </summary>
    public string? InkscapeCliPath { get; init; }
    /// <summary>Optional managed JAVA_HOME for FFDec child process (tools-cache JRE).</summary>
    public string? JavaHome { get; init; }

    /// <summary>Optional java.exe path paired with <see cref="JavaHome"/>.</summary>
    public string? JavaExePath { get; init; }

    /// <summary>Destination category for manual PNG detection and optional promote.</summary>
    public VisualAssetCategory VisualCategory { get; init; } = VisualAssetCategory.Mobs;
}

public sealed class NpcGfxPngValidationResult
{
    public required bool Decoded { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int OpaquePixelCount { get; init; }
    public double OpaqueRatio { get; init; }
    public int VisibleWidth { get; init; }
    public int VisibleHeight { get; init; }
    public int VisibleArea { get; init; }
    public long FileBytes { get; init; }
    public required NpcGfxPreviewPrepStatus ContentStatus { get; init; }
    public string? Reason { get; init; }
}

public interface IFfdecProcessRunner
{
    FfdecRunResult RunExportPng(
        string ffdecCliPath,
        string swfPath,
        string outputDirectory,
        SwfArtworkExportTargetSelector.ExportKind kind,
        int? spriteCharacterId,
        double zoom,
        TimeSpan timeout,
        CancellationToken ct = default);

    FfdecRunResult RunExportFrameSvg(
        string ffdecCliPath,
        string swfPath,
        string outputDirectory,
        TimeSpan timeout,
        CancellationToken ct = default);
}

public sealed class FfdecRunResult
{
    public required bool TimedOut { get; init; }
    public required int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
}

/// <summary>ADMIN.UI.4B.2A.3G.5 — result of promoting OK staging PNGs into Library/Visuals/Mobs.</summary>
public sealed class NpcGfxPromoteFromBatchResult
{
    public required string BatchRoot { get; init; }
    public required string LibraryRoot { get; init; }
    public int OkDetected { get; init; }
    public required IReadOnlyList<int> Created { get; init; }
    public required IReadOnlyList<int> SkippedManual { get; init; }
    public required IReadOnlyList<(int GfxId, string Status)> SkippedStatus { get; init; }
    public required IReadOnlyList<int> SkippedMissingPng { get; init; }
    public int ManualOverwrites { get; init; }
}
