namespace RufusMapEditor.LegacyCompatibility.LangMaps;

public sealed class LangMapEntry
{
    public required int MapId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int SubArea { get; init; }
    public required int Ep { get; init; }
    public IReadOnlyDictionary<string, object?> ExtraProperties { get; init; } =
        new Dictionary<string, object?>();
}

public sealed class LangMapsGenerateRequest
{
    public required string SourceSwfPath { get; init; }
    public required string OutputDirectory { get; init; }
    public required int MapId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int SubArea { get; init; }
    /// <summary>Required. Not inferred in FASE 11A.</summary>
    public int? Ep { get; init; }
}

/// <summary>MAP-BATCH.1 — one maps_es N→N+1 with multiple MA.m updates.</summary>
public sealed class LangMapsBatchEntry
{
    public required int MapId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int SubArea { get; init; }
    public required int Ep { get; init; }
}

public sealed class LangMapsBatchGenerateRequest
{
    public required string SourceSwfPath { get; init; }
    public required string OutputDirectory { get; init; }
    public required IReadOnlyList<LangMapsBatchEntry> Entries { get; init; }
}

public sealed class LangMapsGenerateResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? OutputPath { get; init; }
    public int? SourceVersion { get; init; }
    public int? TargetVersion { get; init; }
    public bool Inserted { get; init; }
    public bool Updated { get; init; }
    /// <summary>Per-map insert/update flags for batch generation.</summary>
    public IReadOnlyList<(int MapId, bool Inserted, bool Updated)>? EntryResults { get; init; }
}

public sealed class LangMapsInspectResult
{
    public required string SourcePath { get; init; }
    public required int Version { get; init; }
    public required int EntryCount { get; init; }
    public required IReadOnlyList<LangMapEntry> Entries { get; init; }
    public required bool WasCompressed { get; init; }
    public required byte SwfVersion { get; init; }
}

internal sealed class LangMaEntrySpan
{
    public required int MapId { get; init; }
    public required int ActionStart { get; init; }
    public required int ActionEnd { get; init; }
    public required int NProps { get; init; }
    public required Dictionary<string, int> IntProps { get; init; }
    public required Dictionary<string, string> StringProps { get; init; }
    public required Dictionary<string, int> IntValueOffsets { get; init; }
}

internal sealed class LangMapsParsed
{
    public required SwfContainer Container { get; init; }
    public required int DoActionTagIndex { get; init; }
    public required byte[] ActionData { get; init; }
    public required IReadOnlyList<string> ConstantPool { get; init; }
    public required int PoolEnd { get; init; }
    public required IReadOnlyList<Avm1Action> Actions { get; init; }
    public required int VersionValue { get; init; }
    public required int VersionIntOffset { get; init; }
    public required int FileEndPushOffset { get; init; }
    public required IReadOnlyList<LangMaEntrySpan> Entries { get; init; }
    public required int IdxMa { get; init; }
    public required int IdxM { get; init; }
    public required int IdxX { get; init; }
    public required int IdxY { get; init; }
    public required int IdxSa { get; init; }
    public required int IdxEp { get; init; }
    public required int IdxVersion { get; init; }
}
