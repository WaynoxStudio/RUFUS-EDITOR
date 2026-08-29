namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.7B/7B.1 — N.d[id] = { n, a? } assignment for npc_es.</summary>
public sealed class NpcEsAssignment
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Sorted ascending client action ids (empty = omit property a).</summary>
    public IReadOnlyList<int> Actions { get; init; } = Array.Empty<int>();
}

public sealed class NpcEsSnapshot
{
    public required int Version { get; init; }
    public required int SwfVersion { get; init; }
    public required bool WasCompressed { get; init; }
    public required string Signature { get; init; }
    /// <summary>N.d[id].n — visible NPC name.</summary>
    public required IReadOnlyDictionary<int, string> Names { get; init; }
    /// <summary>N.d[id].a — client action ids (missing key = no a property).</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<int>> Actions { get; init; }
    /// <summary>N.a[id] — global action labels (preserved; not mutated).</summary>
    public required IReadOnlyDictionary<int, string> ActionLabels { get; init; }
    public required int NameAssignmentCount { get; init; }
    public required bool HasFileEnd { get; init; }
    public required int ConstantPoolCount { get; init; }
    public required int DoActionCount { get; init; }

    public bool Contains(int id) => Names.ContainsKey(id);

    public IReadOnlyList<int> ActionsOf(int id) =>
        Actions.TryGetValue(id, out var a) ? a : Array.Empty<int>();
}

public sealed class NpcEsGenerateRequest
{
    public required byte[] SourceSwfBytes { get; init; }
    public required IReadOnlyList<NpcEsAssignment> Additions { get; init; }
    public string? OutputDirectory { get; init; }
}

public sealed class NpcEsGenerateResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public byte[]? OutputBytes { get; init; }
    public string? OutputPath { get; init; }
    public int SourceVersion { get; init; }
    public int TargetVersion { get; init; }
    public NpcEsSnapshot? SourceSnapshot { get; init; }
    public NpcEsSnapshot? OutputSnapshot { get; init; }
}
