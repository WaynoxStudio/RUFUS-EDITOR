using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>One resolved SWF in the SWF-keyed prep pipeline.</summary>
public sealed class ItemIconSwfWorkItem
{
    public required ItemIconPreviewKey PreviewKey { get; init; }
    public required int GfxId { get; init; }
    public required int TypeId { get; init; }
    public required int SampleItemId { get; init; }
    public string SampleNombre { get; init; } = "";
    public required string ResolvedSwfRelativePath { get; init; }
    public string? ResolvedSwfFullPath { get; init; }
    public required bool SwfExists { get; init; }
    public required IReadOnlyList<int> AssociatedItemIds { get; init; }
}

public sealed class ItemIconSwfPreviewPrepEntry
{
    public required string PreviewKey { get; init; }
    public required string ResolvedSwfRelativePath { get; init; }
    public required int GfxId { get; init; }
    public required int TypeId { get; init; }
    public required int SampleItemId { get; init; }
    public required IReadOnlyList<int> AssociatedItemIds { get; init; }
    public string SampleNombre { get; init; } = "";
    public string? SourceSwf { get; init; }
    public required NpcGfxPreviewPrepStatus Status { get; init; }
    public string? Reason { get; init; }
    public string? OutputPng { get; init; }
    public string? ExportMethod { get; init; }
    public int? ExportSpriteId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long FileBytes { get; init; }
    public bool SkippedExistingLibrary { get; init; }
}

public sealed class ItemIconSwfPreviewPrepSummary
{
    public required int TotalCatalogItems { get; init; }
    public required int SampleSwfRequested { get; init; }
    public required int UniqueSwfInCatalog { get; init; }
    public required int UniqueSwfProcessed { get; init; }
    public required int SkippedDuplicateSwf { get; init; }
    public required int SkippedExistingLibrary { get; init; }
    public required int Ok { get; init; }
    public required int Review { get; init; }
    public required int Failed { get; init; }
    public required int NoIcon { get; init; }
    public required int ManualExists { get; init; }
    public required IReadOnlyDictionary<string, int> ExportMethodCounts { get; init; }
    public required IReadOnlyList<ItemIconSwfPreviewPrepEntry> Entries { get; init; }
    public required string StagingRoot { get; init; }
}

public sealed class ItemIconSwfPromoteResult
{
    public required string BatchRoot { get; init; }
    public required string LibraryRoot { get; init; }
    public int PromotableDetected { get; init; }
    public required IReadOnlyList<string> Created { get; init; }
    public required IReadOnlyList<string> SkippedExisting { get; init; }
    public required IReadOnlyList<(string PreviewKey, string Status)> SkippedStatus { get; init; }
    public required IReadOnlyList<string> SkippedMissingPng { get; init; }
}

public sealed class ItemIconSwfPreviewPrepOptions
{
    public required string FfdecCliPath { get; init; }
    public required string ClipsRoot { get; init; }
    public required string StagingRoot { get; init; }
    public string? LibraryRoot { get; init; }
    public string? InkscapeCliPath { get; init; }
    public double Zoom { get; init; } = 1;
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public bool PromoteOkToLibrary { get; init; }
    public bool SkipExistingLibraryPng { get; init; } = true;
}
