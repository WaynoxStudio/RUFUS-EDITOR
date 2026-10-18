using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>CONTENT.ITEMS.PREVIEW.1 — one catalog item in a prep batch (dev-only).</summary>
public sealed class ItemIconPreviewSample
{
    public required int ItemId { get; init; }
    public required int GfxId { get; init; }
    public required int TypeId { get; init; }
    public string Nombre { get; init; } = "";
    public string Category { get; init; } = "";
    public string IconRelativePath { get; init; } = "";
    public string? IconFullPath { get; init; }
}

/// <summary>Prep outcome for one gfxId (may represent multiple items sharing the icon).</summary>
public sealed class ItemIconPreviewPrepEntry
{
    public required int GfxId { get; init; }
    public int SampleItemId { get; init; }
    public int TypeId { get; init; }
    public int ItemsSharingGfx { get; init; } = 1;
    public string SampleNombre { get; init; } = "";
    public string IconRelativePath { get; init; } = "";
    public string? SourceSwf { get; init; }
    public required NpcGfxPreviewPrepStatus Status { get; init; }
    public string? Reason { get; init; }
    public string? OutputPng { get; init; }
    public string? ExportMethod { get; init; }
    public int? ExportSpriteId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long FileBytes { get; init; }
    public bool SkippedDuplicateGfx { get; init; }
    public bool SkippedExistingLibrary { get; init; }
}

public sealed class ItemIconPreviewPrepSummary
{
    public required int TotalCatalogItems { get; init; }
    public required int SampleItemsRequested { get; init; }
    public required int UniqueGfxInCatalog { get; init; }
    public required int UniqueGfxProcessed { get; init; }
    public required int SkippedDuplicateGfx { get; init; }
    public required int SkippedExistingLibrary { get; init; }
    public required int Ok { get; init; }
    public required int Review { get; init; }
    public required int Failed { get; init; }
    public required int NoIcon { get; init; }
    public required int ManualExists { get; init; }
    public required IReadOnlyDictionary<string, int> ExportMethodCounts { get; init; }
    public required IReadOnlyList<ItemIconPreviewPrepEntry> Entries { get; init; }
    public required string StagingRoot { get; init; }
}

public sealed class ItemIconPromoteResult
{
    public required string BatchRoot { get; init; }
    public required string LibraryRoot { get; init; }
    public int PromotableDetected { get; init; }
    public required IReadOnlyList<int> Created { get; init; }
    public required IReadOnlyList<int> SkippedExisting { get; init; }
    public required IReadOnlyList<(int GfxId, string Status)> SkippedStatus { get; init; }
    public required IReadOnlyList<int> SkippedMissingPng { get; init; }
}

public sealed class ItemIconPreviewPrepOptions
{
    public required string FfdecCliPath { get; init; }
    public required string ClipsRoot { get; init; }
    public required string StagingRoot { get; init; }
    public string? LibraryRoot { get; init; }
    public string? InkscapeCliPath { get; init; }
    public double Zoom { get; init; } = 1;
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public bool PromoteOkToLibrary { get; init; }
    /// <summary>Skip FFDec when Library/Visuals/Items/{gfx}.png already validates OK/REVIEW.</summary>
    public bool SkipExistingLibraryPng { get; init; } = true;
}
