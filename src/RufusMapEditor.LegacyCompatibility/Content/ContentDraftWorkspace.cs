using System.Text.Json;
using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// Shared Content draft workspace (NPC + diálogos + misiones). No BD/SFTP writes.
/// </summary>
public sealed class ContentDraftWorkspace
{
    public NpcDraftBatch Npcs { get; } = new();
    public DialogDraftBatch Dialogs { get; } = new();
    public MissionDraftBatch Missions { get; } = new();

    public ContentWorkspaceSnapshot ToSnapshot() => new()
    {
        NpcDbMaxId = Npcs.DbMaxId,
        QuestionDbMaxId = Dialogs.DbMaxQuestionId,
        StageDbMaxId = Missions.DbMaxStageId,
        ObjectiveDbMaxId = Missions.DbMaxObjectiveId,
        Npcs = Npcs.Drafts.Select(n => n.CloneData()).ToList(),
        Questions = Dialogs.Questions.Select(CloneQuestion).ToList(),
        Missions = Missions.Missions.Select(CloneMission).ToList(),
    };

    public void LoadSnapshot(ContentWorkspaceSnapshot snap)
    {
        ArgumentNullException.ThrowIfNull(snap);
        Npcs.Clear();
        Npcs.SetDbMaxId(snap.NpcDbMaxId);
        foreach (var n in snap.Npcs)
            Npcs.ImportPreservingId(n);

        Dialogs.SetDbMaxQuestionId(snap.QuestionDbMaxId);
        Dialogs.LoadQuestions(snap.Questions.Select(CloneQuestion));

        Missions.SetDbMaxStageId(snap.StageDbMaxId);
        Missions.SetDbMaxObjectiveId(snap.ObjectiveDbMaxId);
        Missions.LoadMissions(snap.Missions.Select(CloneMission));
    }

    private static DialogQuestionDraft CloneQuestion(DialogQuestionDraft q)
    {
        var copy = new DialogQuestionDraft
        {
            Id = q.Id,
            OwnerNpcId = q.OwnerNpcId,
            TextLocal = q.TextLocal,
            Params = q.Params,
            Alternos = q.Alternos,
            PublishedBd = q.PublishedBd,
        };
        foreach (var r in q.Responses)
        {
            var rr = new DialogResponseDraft
            {
                DraftId = r.DraftId == Guid.Empty ? Guid.NewGuid() : r.DraftId,
                TextLocal = r.TextLocal,
                PublishedResponseId = r.PublishedResponseId,
            };
            foreach (var a in r.Actions)
                rr.Actions.Add(a.Clone());
            copy.Responses.Add(rr);
        }
        return copy;
    }

    private static MissionDraft CloneMission(MissionDraft m)
    {
        var copy = new MissionDraft
        {
            DraftId = m.DraftId == Guid.Empty ? Guid.NewGuid() : m.DraftId,
            PublishedQuestId = m.PublishedQuestId,
            PublishedBd = m.PublishedBd,
            Nombre = m.Nombre,
            PuedeRepetirse = m.PuedeRepetirse,
            StartNpcId = m.StartNpcId,
            PregDarPreguntaId = m.PregDarPreguntaId,
            PregIncompletaPreguntaId = m.PregIncompletaPreguntaId,
            PregCompletadaPreguntaId = m.PregCompletadaPreguntaId,
        };
        foreach (var s in m.Stages)
        {
            var ns = new MissionStageDraft
            {
                Id = s.Id,
                Nombre = s.Nombre,
                Descripcion = s.Descripcion,
                Rewards = s.Rewards.Clone(),
                VariosObj = s.VariosObj,
            };
            foreach (var o in s.Objectives)
                ns.Objectives.Add(o.CloneNewIdentity(o.Id));
            copy.Stages.Add(ns);
        }
        return copy;
    }
}

public sealed class ContentWorkspaceSnapshot
{
    public int NpcDbMaxId { get; set; }
    public int QuestionDbMaxId { get; set; }
    public int StageDbMaxId { get; set; }
    public int ObjectiveDbMaxId { get; set; }
    public List<NpcsModeloDraft> Npcs { get; set; } = new();
    public List<DialogQuestionDraft> Questions { get; set; } = new();
    public List<MissionDraft> Missions { get; set; } = new();
}

public static class ContentWorkspaceSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ContentDraftWorkspace workspace) =>
        JsonSerializer.Serialize(workspace.ToSnapshot(), Options);

    public static ContentDraftWorkspace Deserialize(string json)
    {
        var snap = JsonSerializer.Deserialize<ContentWorkspaceSnapshot>(json, Options)
                   ?? new ContentWorkspaceSnapshot();
        var ws = new ContentDraftWorkspace();
        ws.LoadSnapshot(snap);
        return ws;
    }
}
