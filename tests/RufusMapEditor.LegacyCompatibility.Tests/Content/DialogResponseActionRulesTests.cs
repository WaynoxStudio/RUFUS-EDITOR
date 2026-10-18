using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class DialogResponseActionRulesTests
{
    [Fact]
    public void CanAddAction_blocks_duplicate_friendly_action()
    {
        var response = new DialogResponseDraft();
        response.Actions.Add(new DialogActionDraft { Accion = DialogActionCodes.GotoQuestion });

        Assert.False(DialogResponseActionRules.CanAddAction(response, DialogActionCodes.GotoQuestion));
        Assert.True(DialogResponseActionRules.CanAddAction(response, DialogActionCodes.StartQuest));
    }

    [Fact]
    public void CanAddAction_blocks_teleport_and_goto_together()
    {
        var response = new DialogResponseDraft();
        response.Actions.Add(new DialogActionDraft { Accion = DialogActionCodes.GotoQuestion });

        Assert.False(DialogResponseActionRules.CanAddAction(response, DialogActionCodes.Teleport));
    }

    [Fact]
    public void ValidateResponse_detects_duplicate_and_incompatible_actions()
    {
        var response = new DialogResponseDraft
        {
            Actions =
            {
                new DialogActionDraft { Accion = DialogActionCodes.GotoQuestion },
                new DialogActionDraft { Accion = DialogActionCodes.GotoQuestion },
            },
        };

        var errors = DialogResponseActionRules.ValidateResponse(response);
        Assert.Contains(errors, e => e.Contains("duplicadas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryAddAction_enforces_rules_in_batch()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q = batch.CreateQuestion(20062);
        var r = batch.AddResponse(q);

        Assert.True(batch.TryAddAction(r, DialogActionCodes.GotoQuestion, out _, out _));
        Assert.False(batch.TryAddAction(r, DialogActionCodes.GotoQuestion, out _, out var dupErr));
        Assert.NotNull(dupErr);
        Assert.False(batch.TryAddAction(r, DialogActionCodes.Teleport, out _, out var clashErr));
        Assert.NotNull(clashErr);
    }

    [Fact]
    public void Publish_plan_blocks_teleport_and_goto_on_same_response()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        ws.Dialogs.AddActionAllowingRuleViolations(r, DialogActionCodes.Teleport).Args = "1,2";
        ws.Dialogs.AddActionAllowingRuleViolations(r, DialogActionCodes.GotoQuestion);

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
        Assert.Contains(plan.Errors, e =>
            e.Contains("incompatibles", StringComparison.OrdinalIgnoreCase)
            && e.Contains("Ir a otra pregunta", StringComparison.OrdinalIgnoreCase)
            && e.Contains("Teletransporte", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Publish_plan_allows_start_quest_with_goto()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        var gotoAction = ws.Dialogs.AddAction(r, DialogActionCodes.GotoQuestion);
        ws.Dialogs.LinkGotoQuestion(gotoAction, ws.Dialogs.CreateQuestion(npc.Id).Id);
        ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest).Args = "3503";

        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20061,
            NpcPreguntas = 20023,
            NpcRespuestas = 90001,
            Misiones = 100003,
            MisionEtapas = 5500,
            MisionObjetivos = 4214,
        });

        Assert.DoesNotContain(plan.Errors, e =>
            e.Contains("incompatibles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CountGotoReferences_detects_exclusive_and_shared_targets()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q1 = batch.CreateQuestion(20062);
        var q2 = batch.CreateQuestion(20062);
        var q3 = batch.CreateQuestion(20062);
        var r1 = batch.AddResponse(q1);
        var r2 = batch.AddResponse(q2);
        var a1 = batch.AddAction(r1, DialogActionCodes.GotoQuestion);
        batch.LinkGotoQuestion(a1, q3.Id);
        var a2 = batch.AddAction(r2, DialogActionCodes.GotoQuestion);
        batch.LinkGotoQuestion(a2, q3.Id);

        Assert.Equal(2, batch.CountGotoReferencesToQuestion(q3.Id));
        Assert.False(batch.IsExclusiveGotoReference(a1));
        Assert.False(batch.IsExclusiveGotoReference(a2));

        var solo = batch.CreateQuestion(20062);
        var rs = batch.AddResponse(solo);
        var only = batch.AddAction(rs, DialogActionCodes.GotoQuestion);
        batch.LinkGotoQuestion(only, solo.Id);
        Assert.True(batch.IsExclusiveGotoReference(only));
    }
}
