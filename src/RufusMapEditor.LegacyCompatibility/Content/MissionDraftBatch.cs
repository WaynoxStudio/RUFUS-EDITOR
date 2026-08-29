namespace RufusMapEditor.LegacyCompatibility.Content;

public enum MissionDeleteResult
{
    NotFound,
    HasReferences,
    Deleted,
}

public readonly struct MissionDeleteBlocked
{
    public MissionDeleteBlocked(Guid missionDraftId, IReadOnlyList<Guid> responseDraftIds)
    {
        MissionDraftId = missionDraftId;
        ResponseDraftIds = responseDraftIds;
    }

    public Guid MissionDraftId { get; }
    public IReadOnlyList<Guid> ResponseDraftIds { get; }
}

/// <summary>
/// Mission drafts with provisional stage/objective IDs (global). Quest uses DraftId only.
/// </summary>
public sealed class MissionDraftBatch
{
    private readonly List<MissionDraft> _missions = new();
    private int _dbMaxStageId;
    private int _dbMaxObjectiveId;

    public IReadOnlyList<MissionDraft> Missions => _missions;
    public int DbMaxStageId => _dbMaxStageId;
    public int DbMaxObjectiveId => _dbMaxObjectiveId;
    public int NextStageId => ComputeNextStageId();
    public int NextObjectiveId => ComputeNextObjectiveId();

    public void SetDbMaxStageId(int maxId)
    {
        if (maxId < 0) throw new ArgumentOutOfRangeException(nameof(maxId));
        _dbMaxStageId = maxId;
    }

    public void SetDbMaxObjectiveId(int maxId)
    {
        if (maxId < 0) throw new ArgumentOutOfRangeException(nameof(maxId));
        _dbMaxObjectiveId = maxId;
    }

    public void LoadMissions(IEnumerable<MissionDraft> missions)
    {
        _missions.Clear();
        foreach (var m in missions)
            _missions.Add(m);
    }

    public MissionDraft CreateMission()
    {
        var m = new MissionDraft();
        _missions.Add(m);
        return m;
    }

    public MissionDraft DuplicateMission(MissionDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = DeepDuplicate(source);
        _missions.Add(copy);
        return copy;
    }

    private MissionDraft DeepDuplicate(MissionDraft source)
    {
        var copy = new MissionDraft
        {
            DraftId = Guid.NewGuid(),
            PublishedQuestId = null,
            PublishedBd = false,
            Nombre = source.Nombre,
            PuedeRepetirse = source.PuedeRepetirse,
            StartNpcId = source.StartNpcId,
            PregDarPreguntaId = source.PregDarPreguntaId,
            PregIncompletaPreguntaId = source.PregIncompletaPreguntaId,
            PregCompletadaPreguntaId = source.PregCompletadaPreguntaId,
        };
        var nextStage = ComputeNextStageId();
        var nextObj = ComputeNextObjectiveId();
        foreach (var s in source.Stages)
        {
            var ns = new MissionStageDraft
            {
                Id = nextStage++,
                Nombre = s.Nombre,
                Descripcion = s.Descripcion,
                Rewards = s.Rewards.Clone(),
                VariosObj = s.VariosObj,
            };
            foreach (var o in s.Objectives)
                ns.Objectives.Add(o.CloneNewIdentity(nextObj++));
            copy.Stages.Add(ns);
        }
        return copy;
    }

    public MissionDeleteResult TryDeleteMission(
        Guid draftId,
        bool unlinkAndDelete,
        Func<Guid, IReadOnlyList<Guid>>? findDialogRefs,
        out MissionDeleteBlocked? blocked)
    {
        blocked = null;
        var m = FindByDraftId(draftId);
        if (m is null) return MissionDeleteResult.NotFound;

        var refs = findDialogRefs?.Invoke(draftId) ?? Array.Empty<Guid>();
        if (refs.Count > 0 && !unlinkAndDelete)
        {
            blocked = new MissionDeleteBlocked(draftId, refs);
            return MissionDeleteResult.HasReferences;
        }

        _missions.RemoveAll(x => x.DraftId == draftId);
        return MissionDeleteResult.Deleted;
    }

    public MissionStageDraft AddStage(MissionDraft mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var id = ComputeNextStageId();
        EnsureStageUnique(id);
        var s = new MissionStageDraft { Id = id };
        mission.Stages.Add(s);
        return s;
    }

    public bool RemoveStage(MissionDraft mission, MissionStageDraft stage)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(stage);
        if (mission.Stages.Remove(stage)) return true;
        for (var i = 0; i < mission.Stages.Count; i++)
        {
            if (mission.Stages[i].Id != stage.Id) continue;
            mission.Stages.RemoveAt(i);
            return true;
        }
        return false;
    }

    public bool MoveStage(MissionDraft mission, MissionStageDraft stage, int delta)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var idx = IndexOfStage(mission, stage.Id);
        if (idx < 0) return false;
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= mission.Stages.Count) return false;
        mission.Stages.RemoveAt(idx);
        mission.Stages.Insert(newIdx, stage);
        return true;
    }

    public MissionStageDraft DuplicateStage(MissionDraft mission, MissionStageDraft source)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(source);
        var nextObj = ComputeNextObjectiveId();
        var copy = new MissionStageDraft
        {
            Id = ComputeNextStageId(),
            Nombre = source.Nombre,
            Descripcion = source.Descripcion,
            Rewards = source.Rewards.Clone(),
            VariosObj = source.VariosObj,
        };
        EnsureStageUnique(copy.Id);
        foreach (var o in source.Objectives)
            copy.Objectives.Add(o.CloneNewIdentity(nextObj++));
        var idx = IndexOfStage(mission, source.Id);
        if (idx < 0) mission.Stages.Add(copy);
        else mission.Stages.Insert(idx + 1, copy);
        return copy;
    }

    public MissionObjectiveDraft AddObjective(MissionStageDraft stage, int tipo = 0)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var id = ComputeNextObjectiveId();
        EnsureObjectiveUnique(id);
        var o = new MissionObjectiveDraft { Id = id, Tipo = tipo };
        stage.Objectives.Add(o);
        return o;
    }

    public MissionObjectiveDraft AddDeliverItemsObjective(
        MissionStageDraft stage, int npcId, int itemId, int qty)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var id = ComputeNextObjectiveId();
        EnsureObjectiveUnique(id);
        var o = MissionObjectiveDraft.CreateDeliverItems(id, npcId, itemId, qty);
        stage.Objectives.Add(o);
        return o;
    }

    public bool RemoveObjective(MissionStageDraft stage, MissionObjectiveDraft objective)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(objective);
        if (stage.Objectives.Remove(objective)) return true;
        for (var i = 0; i < stage.Objectives.Count; i++)
        {
            if (stage.Objectives[i].Id != objective.Id) continue;
            stage.Objectives.RemoveAt(i);
            return true;
        }
        return false;
    }

    public MissionDraft? FindByDraftId(Guid id) =>
        _missions.FirstOrDefault(m => m.DraftId == id);

    public bool HasDuplicateStageIds()
    {
        var seen = new HashSet<int>();
        foreach (var m in _missions)
        foreach (var s in m.Stages)
            if (!seen.Add(s.Id)) return true;
        return false;
    }

    public bool HasDuplicateObjectiveIds()
    {
        var seen = new HashSet<int>();
        foreach (var m in _missions)
        foreach (var s in m.Stages)
        foreach (var o in s.Objectives)
            if (!seen.Add(o.Id)) return true;
        return false;
    }

    public bool HasDuplicateMissionDraftIds()
    {
        var seen = new HashSet<Guid>();
        foreach (var m in _missions)
            if (!seen.Add(m.DraftId)) return true;
        return false;
    }

    public IReadOnlyList<int> AllStageIds =>
        _missions.SelectMany(m => m.Stages.Select(s => s.Id)).ToList();

    public IReadOnlyList<int> AllObjectiveIds =>
        _missions.SelectMany(m => m.Stages.SelectMany(s => s.Objectives.Select(o => o.Id))).ToList();

    public void ClearPreguntaReferences(int preguntaId)
    {
        foreach (var m in _missions)
        {
            if (m.PregDarPreguntaId == preguntaId) m.PregDarPreguntaId = null;
            if (m.PregIncompletaPreguntaId == preguntaId) m.PregIncompletaPreguntaId = null;
            if (m.PregCompletadaPreguntaId == preguntaId) m.PregCompletadaPreguntaId = null;
        }
    }

    public void ClearNpcReferences(int npcId)
    {
        foreach (var m in _missions)
        {
            if (m.StartNpcId == npcId)
            {
                m.StartNpcId = null;
                m.PregDarPreguntaId = null;
                m.PregIncompletaPreguntaId = null;
                m.PregCompletadaPreguntaId = null;
            }
        }
    }

    public IReadOnlyList<Guid> MissionsReferencingNpc(int npcId) =>
        _missions.Where(m => m.StartNpcId == npcId).Select(m => m.DraftId).ToList();

    public IReadOnlyList<Guid> MissionsReferencingPregunta(int preguntaId) =>
        _missions.Where(m =>
                m.PregDarPreguntaId == preguntaId
                || m.PregIncompletaPreguntaId == preguntaId
                || m.PregCompletadaPreguntaId == preguntaId)
            .Select(m => m.DraftId).ToList();

    /// <summary>
    /// CONT.5.1 — removes mission drafts not linked to any current NPC id in <paramref name="validNpcIds"/>.
    /// Does not touch missions whose StartNpcId belongs to another NPC still in the lote.
    /// </summary>
    public int RemoveOrphanMissions(IReadOnlyCollection<int> validNpcIds)
    {
        ArgumentNullException.ThrowIfNull(validNpcIds);
        var valid = validNpcIds as HashSet<int> ?? validNpcIds.ToHashSet();
        var removed = 0;
        for (var i = _missions.Count - 1; i >= 0; i--)
        {
            var m = _missions[i];
            if (m.PublishedBd) continue;
            if (m.StartNpcId is int sid && valid.Contains(sid))
                continue;
            _missions.RemoveAt(i);
            removed++;
        }
        return removed;
    }

    private static int IndexOfStage(MissionDraft mission, int stageId)
    {
        for (var i = 0; i < mission.Stages.Count; i++)
        {
            if (mission.Stages[i].Id == stageId) return i;
        }
        return -1;
    }

    private int ComputeNextStageId()
    {
        var maxDraft = AllStageIds.DefaultIfEmpty(0).Max();
        return Math.Max(_dbMaxStageId, maxDraft) + 1;
    }

    private int ComputeNextObjectiveId()
    {
        var maxDraft = AllObjectiveIds.DefaultIfEmpty(0).Max();
        return Math.Max(_dbMaxObjectiveId, maxDraft) + 1;
    }

    private void EnsureStageUnique(int id)
    {
        if (AllStageIds.Contains(id))
            throw new InvalidOperationException($"ID provisional de etapa duplicado: {id}");
    }

    private void EnsureObjectiveUnique(int id)
    {
        if (AllObjectiveIds.Contains(id))
            throw new InvalidOperationException($"ID provisional de objetivo duplicado: {id}");
    }
}
