namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class QuestsEsQuestEntry
{
    public required string Name { get; init; }
    /// <summary>Stage IDs from Q.q.s when object form; empty when <see cref="IsStringName"/>.</summary>
    public required IReadOnlyList<int> StageIds { get; init; }
    /// <summary>
    /// CONTENT.QUESTS_ES.QQ_STRING.FIX.11 — true when bytecode is
    /// <c>Q.q[id] = "name"</c> (functional custom quests like 3503), not <c>{n,s}</c>.
    /// </summary>
    public bool IsStringName { get; init; }
}

public sealed class QuestsEsStageEntry
{
    public required string Name { get; init; }
    /// <summary>Latin1 string or integer literal in SWF.</summary>
    public required object Description { get; init; }
    public required IReadOnlyList<object?> Rewards { get; init; }
    public required IReadOnlyList<object?> LevelBands { get; init; }
}

public sealed class QuestsEsObjectiveEntry
{
    public required int Type { get; init; }
    public required IReadOnlyList<object?> Parameters { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
}

public sealed class QuestsEsSnapshot
{
    public required int Version { get; init; }
    public required int SwfVersion { get; init; }
    public required bool WasCompressed { get; init; }
    public required string Signature { get; init; }
    public required IReadOnlyDictionary<int, QuestsEsQuestEntry> Quests { get; init; }
    public required IReadOnlyDictionary<int, QuestsEsStageEntry> Stages { get; init; }
    public required IReadOnlyDictionary<int, QuestsEsObjectiveEntry> Objectives { get; init; }
    /// <summary>Q.t[id] — global type templates (read-only; never mutated).</summary>
    public required IReadOnlyDictionary<int, string> TypeTemplates { get; init; }
    public required int QuestAssignmentCount { get; init; }
    public required int StageAssignmentCount { get; init; }
    public required int ObjectiveAssignmentCount { get; init; }
    public required bool HasFileEnd { get; init; }
    public required int ConstantPoolCount { get; init; }
    public required int DoActionCount { get; init; }
    /// <summary>Q.q IDs whose last AVM1 assign is not vanilla (s-before-n) layout.</summary>
    public IReadOnlySet<int> NonCanonicalQuestIds { get; init; } = new HashSet<int>();

    public bool ContainsQuest(int id) => Quests.ContainsKey(id);
    public bool ContainsStage(int id) => Stages.ContainsKey(id);
    public bool ContainsObjective(int id) => Objectives.ContainsKey(id);
    public bool NeedsQuestLayoutRepair(int questId) => NonCanonicalQuestIds.Contains(questId);
}

public sealed class QuestsEsQuestAssignment
{
    public required int QuestId { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<int> StageIds { get; init; }
}

public sealed class QuestsEsStageAssignment
{
    public required int StageId { get; init; }
    public required string Name { get; set; }
    public required string Description { get; init; }
    public required MissionRewardsDraft Rewards { get; init; }
}

public sealed class QuestsEsObjectiveAssignment
{
    public required int ObjectiveId { get; init; }
    public required int Type { get; init; }
    public required string Args { get; init; }
    public required string Detalle { get; init; }
}

public sealed class QuestsEsMissionAssignment
{
    public required QuestsEsQuestAssignment Quest { get; init; }
    /// <summary>ENTRY_LEVEL_DIFF.12 — false when only Q.s/Q.o change (preserve existing Q.q bytecode).</summary>
    public bool WriteQuest { get; init; } = true;
    public required IReadOnlyList<QuestsEsStageAssignment> Stages { get; init; }
    public required IReadOnlyList<QuestsEsObjectiveAssignment> Objectives { get; init; }
}

public sealed class QuestsEsGenerateRequest
{
    public required byte[] SourceSwfBytes { get; init; }
    public required IReadOnlyList<QuestsEsMissionAssignment> Additions { get; init; }
    public string? OutputDirectory { get; init; }
}

public sealed class QuestsEsGenerateResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public byte[]? OutputBytes { get; init; }
    public string? OutputPath { get; init; }
    public int SourceVersion { get; init; }
    public int TargetVersion { get; init; }
    public QuestsEsSnapshot? SourceSnapshot { get; init; }
    public QuestsEsSnapshot? OutputSnapshot { get; init; }
}
