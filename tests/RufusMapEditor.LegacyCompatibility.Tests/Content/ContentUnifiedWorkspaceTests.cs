using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>
/// CONT-UI.1 data contract: one workspace, several NPCs, obligatory dialog, optional mission.
/// Domain only — no BD/SFTP.
/// </summary>
public sealed class ContentUnifiedWorkspaceTests
{
    [Fact]
    public void Several_npcs_keep_own_dialogs_and_optional_mission()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        ws.Dialogs.SetDbMaxQuestionId(20023);

        var a = ws.Npcs.CreateNew();
        var b = ws.Npcs.CreateNew();
        var qa = ws.Dialogs.CreateQuestion(a.Id);
        ws.Dialogs.SetInitialQuestion(a, qa.Id);
        var qb = ws.Dialogs.CreateQuestion(b.Id);
        ws.Dialogs.SetInitialQuestion(b, qb.Id);

        var m = ws.Missions.CreateMission();
        m.Nombre = a.Nombre;
        m.StartNpcId = a.Id;
        m.PregDarPreguntaId = a.Pregunta;

        Assert.Equal(2, ws.Npcs.Drafts.Count);
        Assert.Equal(qa.Id, a.Pregunta);
        Assert.Equal(qb.Id, b.Pregunta);
        Assert.Equal(a.Id, ws.Dialogs.QuestionsForNpc(a.Id).Single().OwnerNpcId);
        Assert.Equal(b.Id, ws.Dialogs.QuestionsForNpc(b.Id).Single().OwnerNpcId);
        Assert.Contains(ws.Missions.Missions, x => x.StartNpcId == a.Id);
        Assert.DoesNotContain(ws.Missions.Missions, x => x.StartNpcId == b.Id);

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        Assert.Equal(2, loaded.Npcs.Drafts.Count);
        Assert.Equal(a.Pregunta, loaded.Npcs.Drafts[0].Pregunta);
        Assert.Equal(b.Pregunta, loaded.Npcs.Drafts[1].Pregunta);
        Assert.Single(loaded.Missions.Missions, x => x.StartNpcId == a.Id);
        Assert.DoesNotContain(loaded.Missions.Missions, x => x.StartNpcId == b.Id);
    }

    [Fact]
    public void Unpublished_npc_without_initial_question_is_incomplete()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        Assert.False(npc.PublishedBd);
        Assert.True(npc.Pregunta <= 0 || ws.Dialogs.FindQuestion(npc.Pregunta) is null);

        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        Assert.NotNull(ws.Dialogs.FindQuestion(npc.Pregunta));
    }
}
