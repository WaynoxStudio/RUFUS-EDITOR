using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class ContentPublishTests
{
    private static ContentDraftWorkspace BuildSampleWorkspace()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "Test NPC";
        npc.DialogMode = NpcDialogMode.Interactive;
        var loc = ws.Npcs.AddLocation(npc);
        loc.MapId = 12501;
        loc.CellId = 104;
        loc.Orientation = 1;

        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q1 = ws.Dialogs.CreateQuestion(npc.Id);
        q1.TextLocal = "Dar mision";
        var q2 = ws.Dialogs.CreateQuestion(npc.Id);
        q2.TextLocal = "Goto target";
        ws.Dialogs.SetInitialQuestion(npc, q1.Id);

        var r = ws.Dialogs.AddResponse(q1);
        var aGoto = ws.Dialogs.AddAction(r, DialogActionCodes.GotoQuestion);
        ws.Dialogs.LinkGotoQuestion(aGoto, q2.Id);
        var aQuest = ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);

        ws.Missions.SetDbMaxStageId(5500);
        ws.Missions.SetDbMaxObjectiveId(4214);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Quest test";
        mission.StartNpcId = npc.Id;
        mission.PregDarPreguntaId = q1.Id;
        var stage = ws.Missions.AddStage(mission);
        stage.Nombre = "E1";
        stage.Rewards.Exp = 100;
        var o1 = ws.Missions.AddDeliverItemsObjective(stage, npc.Id, 8682, 5);
        var o2 = ws.Missions.AddObjective(stage, tipo: 0);
        o2.Args = "raw";
        o2.Detalle = "[\"texto\"]";
        ws.Dialogs.LinkStartMission(aQuest, mission.DraftId);

        var stage2 = ws.Missions.AddStage(mission);
        stage2.Nombre = "E2";
        ws.Missions.AddObjective(stage2, tipo: 1).Args = "[1]";

        return ws;
    }

    [Fact]
    public void Max_plus_one_and_consecutive_blocks()
    {
        var ws = BuildSampleWorkspace();
        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20061,
            NpcPreguntas = 20023,
            NpcRespuestas = 90001,
            Misiones = 100003,
            MisionEtapas = 5500,
            MisionObjetivos = 4214,
        };
        var plan = ContentPublishPlanBuilder.Build(ws, maxes);
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal(20062, plan.ReservedNpcIds[0]);
        Assert.Equal(20024, plan.ReservedQuestionIds[0]);
        Assert.Equal(20025, plan.ReservedQuestionIds[1]);
        Assert.Equal(90002, plan.ReservedResponseIds[0]);
        Assert.Equal(100004, plan.ReservedQuestIds[0]);
        Assert.Equal(5501, plan.ReservedStageIds[0]);
        Assert.Equal(5502, plan.ReservedStageIds[1]);
        Assert.Equal(4215, plan.ReservedObjectiveIds[0]);
        Assert.Equal(4216, plan.ReservedObjectiveIds[1]);
        Assert.Equal(4217, plan.ReservedObjectiveIds[2]);
    }

    [Fact]
    public void Max_change_before_publish_recalculates()
    {
        var ws = BuildSampleWorkspace();
        var plan1 = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20061, NpcPreguntas = 20023, NpcRespuestas = 90001,
            Misiones = 100003, MisionEtapas = 5500, MisionObjetivos = 4214,
        });
        var plan2 = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20100, NpcPreguntas = 20100, NpcRespuestas = 91000,
            Misiones = 100010, MisionEtapas = 5600, MisionObjetivos = 4300,
        });
        Assert.Equal(20062, plan1.ReservedNpcIds[0]);
        Assert.Equal(20101, plan2.ReservedNpcIds[0]);
        Assert.Equal(91001, plan2.ReservedResponseIds[0]);
        Assert.Equal(100011, plan2.ReservedQuestIds[0]);
    }

    [Fact]
    public void Multiaction_shares_one_logical_response_id()
    {
        var ws = BuildSampleWorkspace();
        var plan = ContentPublishPlanBuilder.Build(ws, DefaultMaxes());
        Assert.Equal(1, plan.LogicalResponseCount);
        Assert.Equal(2, plan.ResponseActionRowCount);
        Assert.Single(plan.ResponseActions.Select(r => r.Id).Distinct());
        Assert.Contains(plan.ResponseActions, a => a.Accion == 1);
        Assert.Contains(plan.ResponseActions, a => a.Accion == 44);
    }

    [Fact]
    public void Resolves_accion1_and_accion44_and_pregs_and_stages()
    {
        var ws = BuildSampleWorkspace();
        var plan = ContentPublishPlanBuilder.Build(ws, DefaultMaxes());
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));

        var gotoRow = plan.ResponseActions.Single(a => a.Accion == 1);
        Assert.Equal("20025", gotoRow.Args);

        var questRow = plan.ResponseActions.Single(a => a.Accion == 44);
        Assert.Equal("100004", questRow.Args);

        var mission = plan.Missions.Single();
        Assert.Equal("5501,5502", mission.Etapas);
        Assert.Equal("20062;20024", mission.PregDarMision);

        var stage1 = plan.Stages.Single(s => s.Id == 5501);
        Assert.Equal("4215|4216", stage1.Objetivos);

        var deliver = plan.Objectives.Single(o => o.Tipo == 3);
        Assert.Equal("[20062,8682,5]", deliver.Args);

        Assert.Equal(20062, plan.Locations.Single().Npc);
        Assert.Equal(12501, plan.Locations.Single().Mapa);
        Assert.Equal(104, plan.Locations.Single().Celda);
        Assert.Equal("Test NPC", plan.Locations.Single().Nombre);

        Assert.Equal(20024, plan.Npcs.Single().Pregunta);
    }

    [Fact]
    public async Task Collision_blocks_before_insert()
    {
        var ws = BuildSampleWorkspace();
        var store = new InMemoryContentPublishStore(DefaultMaxes());
        store.ExistingNpcIds.Add(20062); // would be first reserved id; MAX stays 20061
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "rufus-cont5-test-j"));
        var outcome = await svc.PublishAsync(ws);
        Assert.False(outcome.Success);
        Assert.Contains("Colisión", outcome.Error ?? "");
        Assert.Equal(0, store.InsertCallCount);
    }

    [Fact]
    public async Task Prevalidation_fail_zero_writes()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);
        // accion=44 without mission link → invalid
        var plan = ContentPublishPlanBuilder.Build(ws, DefaultMaxes());
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains("accion=44"));

        var store = new InMemoryContentPublishStore(DefaultMaxes());
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "rufus-cont5-test-j2"));
        var outcome = await svc.PublishAsync(ws);
        Assert.False(outcome.Success);
        Assert.Equal(0, store.InsertCallCount);
    }

    [Fact]
    public async Task Myisam_uses_locks_and_publish_ok()
    {
        var ws = BuildSampleWorkspace();
        var store = new InMemoryContentPublishStore(DefaultMaxes(), allMyisam: true);
        var journal = Path.Combine(Path.GetTempPath(), "rufus-cont5-j-" + Guid.NewGuid().ToString("N"));
        var svc = new ContentPublishService(store, journal);
        var outcome = await svc.PublishAsync(ws);
        Assert.True(outcome.Success, outcome.Error);
        Assert.True(store.WasLocked);
        Assert.False(store.WasTransactional);
        Assert.True(store.InsertCallCount > 0);
        Assert.True(ws.Npcs.Drafts[0].PublishedBd);
        Assert.Equal(20062, ws.Npcs.Drafts[0].Id);
        Assert.True(ws.Dialogs.Questions.All(q => q.PublishedBd));
        Assert.True(ws.Missions.Missions[0].PublishedBd);
        Assert.Equal(100004, ws.Missions.Missions[0].PublishedQuestId);
    }

    [Fact]
    public async Task Innodb_uses_transaction()
    {
        var ws = BuildSampleWorkspace();
        var store = new InMemoryContentPublishStore(DefaultMaxes(), allMyisam: false);
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "rufus-cont5-j-innodb"));
        var outcome = await svc.PublishAsync(ws);
        Assert.True(outcome.Success, outcome.Error);
        Assert.True(store.WasTransactional);
        Assert.False(store.WasLocked);
    }

    [Fact]
    public async Task Mid_failure_compensating_rollback_only_batch()
    {
        var ws = BuildSampleWorkspace();
        var store = new InMemoryContentPublishStore(DefaultMaxes(), allMyisam: true);
        // Pre-existing row must survive
        store.ExistingQuestIds.Add(18);
        store.Missions[18] = new MisionInsertRow { Id = 18, Nombre = "preexistente" };

        store.FailNextInsertOn(NpcsModeloColumns.DefaultTable);
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "rufus-cont5-j-rb"));
        var outcome = await svc.PublishAsync(ws);
        Assert.False(outcome.Success);
        Assert.True(outcome.CompensatingRollbackAttempted);
        Assert.True(outcome.CompensatingRollbackOk);
        Assert.Contains(18, store.Missions.Keys);
        Assert.Empty(store.Npcs);
        Assert.Empty(store.Questions);
        Assert.DoesNotContain(100004, store.Missions.Keys);
    }

    [Fact]
    public async Task Lock_denied_blocks_myisam_publish()
    {
        var ws = BuildSampleWorkspace();
        var store = new InMemoryContentPublishStore(DefaultMaxes(), allMyisam: true);
        store.SetAllowLocks(false);
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "rufus-cont5-j-nolock"));
        var outcome = await svc.PublishAsync(ws);
        Assert.False(outcome.Success);
        Assert.Contains("LOCK TABLES", outcome.Error ?? "");
        Assert.Equal(0, store.InsertCallCount);
    }

    [Fact]
    public async Task Second_publish_blocked_after_success()
    {
        var ws = BuildSampleWorkspace();
        var store = new InMemoryContentPublishStore(DefaultMaxes());
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "rufus-cont5-j-2nd"));
        var first = await svc.PublishAsync(ws);
        Assert.True(first.Success, first.Error);
        var insertsAfterFirst = store.InsertCallCount;

        var second = await svc.PublishAsync(ws);
        Assert.False(second.Success);
        Assert.Equal(insertsAfterFirst, store.InsertCallCount);
    }

    [Fact]
    public void Orden_not_in_respuesta_insert_row()
    {
        // Structural: NpcRespuestaInsertRow has no Orden property
        Assert.Null(typeof(NpcRespuestaInsertRow).GetProperty("Orden"));
    }

    private static ContentPublishMaxSnapshot DefaultMaxes() => new()
    {
        NpcsModelo = 20061,
        NpcPreguntas = 20023,
        NpcRespuestas = 90001,
        Misiones = 100003,
        MisionEtapas = 5500,
        MisionObjetivos = 4214,
    };
}
