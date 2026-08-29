using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class DialogDraftBatchTests
{
    [Fact]
    public void Max_20023_first_question_is_20024()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        Assert.Equal(20024, batch.NextQuestionId);
        Assert.Equal(20024, batch.CreateQuestion(20062).Id);
    }

    [Fact]
    public void Several_questions_are_consecutive()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        Assert.Equal(20024, batch.CreateQuestion(20062).Id);
        Assert.Equal(20025, batch.CreateQuestion(20062).Id);
        Assert.Equal(20026, batch.CreateQuestion(20062).Id);
        Assert.False(batch.HasDuplicateQuestionIds());
    }

    [Fact]
    public void Multiple_npcs_share_global_question_sequence()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        Assert.Equal(20024, batch.CreateQuestion(20062).Id);
        Assert.Equal(20025, batch.CreateQuestion(20062).Id);
        Assert.Equal(20026, batch.CreateQuestion(20063).Id);
        Assert.Equal(20027, batch.CreateQuestion(20063).Id);
        Assert.Equal(new[] { 20024, 20025, 20026, 20027 }, batch.ProvisionalQuestionIds);
    }

    [Fact]
    public void Responses_use_DraftId_not_numeric_publish_id()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q = batch.CreateQuestion(20062);
        var r = batch.AddResponse(q);
        Assert.NotEqual(Guid.Empty, r.DraftId);
        Assert.DoesNotContain(90002, batch.ProvisionalQuestionIds);
        // No property for numeric response publish id on draft
        Assert.Null(typeof(DialogResponseDraft).GetProperty("Id"));
        Assert.Null(typeof(DialogResponseDraft).GetProperty("PublishId"));
        Assert.False(batch.AnyResponseUsesNumericPublishId());
    }

    [Fact]
    public void Multiaction_preserves_all_actions_same_response()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q = batch.CreateQuestion(20062);
        var r = batch.AddResponse(q);
        var a1 = batch.AddAction(r, DialogActionCodes.GotoQuestion);
        var a2 = batch.AddAction(r, DialogActionCodes.StartQuest);
        a2.Args = "3503";
        Assert.Equal(2, r.Actions.Count);
        Assert.Same(a1, r.Actions[0]);
        Assert.Same(a2, r.Actions[1]);
        Assert.Equal(DialogActionCodes.StartQuest, r.Actions[1].Accion);
        Assert.Equal("3503", r.Actions[1].Args);
    }

    [Fact]
    public void Accion1_links_question_and_writes_args()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q1 = batch.CreateQuestion(20062);
        var q2 = batch.CreateQuestion(20062);
        var r = batch.AddResponse(q1);
        var a = batch.AddAction(r, DialogActionCodes.GotoQuestion);
        batch.LinkGotoQuestion(a, q2.Id);
        Assert.Equal(DialogActionCodes.GotoQuestion, a.Accion);
        Assert.Equal(q2.Id, a.TargetQuestionId);
        Assert.Equal("20025", a.Args);
    }

    [Fact]
    public void Delete_referenced_question_is_protected_then_unlinked()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q1 = batch.CreateQuestion(20062);
        var q2 = batch.CreateQuestion(20062);
        var r = batch.AddResponse(q1);
        var a = batch.AddAction(r, DialogActionCodes.GotoQuestion);
        batch.LinkGotoQuestion(a, q2.Id);

        var blocked = batch.TryDeleteQuestion(q2.Id, unlinkAndDelete: false, out var info);
        Assert.Equal(QuestionDeleteResult.HasReferences, blocked);
        Assert.NotNull(info);
        Assert.Contains(r.DraftId, info!.Value.ResponseDraftIds);
        Assert.NotNull(batch.FindQuestion(q2.Id));

        var deleted = batch.TryDeleteQuestion(q2.Id, unlinkAndDelete: true, out _);
        Assert.Equal(QuestionDeleteResult.Deleted, deleted);
        Assert.Null(batch.FindQuestion(q2.Id));
        Assert.Null(a.TargetQuestionId);
        Assert.Equal("", a.Args);
    }

    [Fact]
    public void Initial_question_updates_npc_draft_only()
    {
        var npc = NpcsModeloDraft.CreateWithDefaults(20062);
        Assert.Equal(0, npc.Pregunta);
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q = batch.CreateQuestion(20062);
        batch.SetInitialQuestion(npc, q.Id);
        Assert.Equal(20024, npc.Pregunta);
    }

    [Fact]
    public void Local_texts_are_stored_on_drafts()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q = batch.CreateQuestion(20062);
        q.TextLocal = "Hola viajero";
        var r = batch.AddResponse(q);
        r.TextLocal = "Adiós";
        Assert.Equal("Hola viajero", batch.FindQuestion(q.Id)!.TextLocal);
        Assert.Equal("Adiós", q.Responses[0].TextLocal);
    }

    [Fact]
    public void Duplicate_question_gets_new_provisional_id_and_new_response_draftids()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q = batch.CreateQuestion(20062);
        q.TextLocal = "A";
        var r = batch.AddResponse(q);
        var oldGuid = r.DraftId;
        var copy = batch.DuplicateQuestion(q);
        Assert.Equal(20025, copy.Id);
        Assert.NotEqual(q.Id, copy.Id);
        Assert.Single(copy.Responses);
        Assert.NotEqual(oldGuid, copy.Responses[0].DraftId);
        Assert.False(batch.HasDuplicateQuestionIds());
    }

    [Fact]
    public void Workspace_roundtrip_keeps_dialog_graph()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "Hola";
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        r.TextLocal = "Ok";
        ws.Dialogs.AddAction(r, DialogActionCodes.Teleport).Args = "1048,227";

        var json = ContentWorkspaceSerializer.Serialize(ws);
        var loaded = ContentWorkspaceSerializer.Deserialize(json);
        Assert.Single(loaded.Npcs.Drafts);
        Assert.Equal(npc.Id, loaded.Npcs.Drafts[0].Id);
        Assert.Equal(q.Id, loaded.Npcs.Drafts[0].Pregunta);
        Assert.Single(loaded.Dialogs.Questions);
        Assert.Equal("Hola", loaded.Dialogs.Questions[0].TextLocal);
        Assert.Equal("Ok", loaded.Dialogs.Questions[0].Responses[0].TextLocal);
        Assert.Equal("1048,227", loaded.Dialogs.Questions[0].Responses[0].Actions[0].Args);
    }

    [Fact]
    public void Create_linked_question_from_action()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q1 = batch.CreateQuestion(20062);
        var r = batch.AddResponse(q1);
        var a = batch.AddAction(r, DialogActionCodes.GotoQuestion);
        var q2 = batch.CreateQuestionLinkedFrom(a, 20062);
        Assert.Equal(20025, q2.Id);
        Assert.Equal(20025, a.TargetQuestionId);
        Assert.Equal("20025", a.Args);
    }

    [Fact]
    public void Response_without_actions_is_incomplete_cont_dialog_1()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q = batch.CreateQuestion(20062);
        var incomplete = batch.AddResponse(q);
        Assert.True(DialogDraftBatch.IsResponseIncomplete(incomplete));
        Assert.True(batch.HasIncompleteResponsesForNpc(20062));

        batch.AddAction(incomplete, DialogActionCodes.Teleport);
        Assert.False(DialogDraftBatch.IsResponseIncomplete(incomplete));
        Assert.False(batch.HasIncompleteResponsesForNpc(20062));
        Assert.Empty(batch.FindIncompleteResponsesForNpc(20062));
    }

    [Fact]
    public void Cont5_plan_still_rejects_response_without_actions()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        ws.Dialogs.AddResponse(q); // 0 actions

        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20061,
            NpcPreguntas = 20023,
            NpcRespuestas = 90001,
            Misiones = 100003,
            MisionEtapas = 5500,
            MisionObjetivos = 4214,
        });
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains("no tiene acciones", StringComparison.OrdinalIgnoreCase));
    }
}
