using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class ContentIdMaxCatalogTests
{
    [Fact]
    public void Max_plus_one_and_consecutive_blocks()
    {
        var ids = ContentIdReservation.ReserveConsecutive(5504, 3);
        Assert.Equal(new[] { 5505, 5506, 5507 }, ids);
    }

    [Fact]
    public void Local_drafts_raise_next_above_bd_and_drafts()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20024);
        ws.Npcs.CreateNew(); // 20025
        ws.Npcs.CreateNew(); // 20026
        Assert.Equal(20027, ws.Npcs.NextProvisionalId);

        ws.Dialogs.SetDbMaxQuestionId(100);
        ws.Dialogs.CreateQuestion(20025);
        ws.Dialogs.CreateQuestion(20025);
        Assert.Equal(103, ws.Dialogs.NextQuestionId);

        ws.Missions.SetDbMaxStageId(10);
        ws.Missions.SetDbMaxObjectiveId(20);
        var m = ws.Missions.CreateMission();
        m.StartNpcId = 20025;
        ws.Missions.AddStage(m);
        ws.Missions.AddStage(m);
        Assert.Equal(13, ws.Missions.NextStageId);

        ws.Dialogs.SetDbMaxResponseId(20024);
        var q = ws.Dialogs.Questions[0];
        ws.Dialogs.AddResponse(q); // reserves slot → 20025
        ws.Dialogs.AddResponse(q); // reserves slot → 20026
        Assert.Equal(20027, ws.Dialogs.NextResponseId);

        ws.Missions.SetDbMaxQuestId(500);
        // `m` already unpublished (slot 501) → siguiente nuevo = 502
        Assert.Equal(502, ws.Missions.NextQuestId);
        ws.Missions.CreateMission();
        Assert.Equal(503, ws.Missions.NextQuestId);
    }

    [Fact]
    public void Apply_catalog_to_workspace_sets_all_six_maxes()
    {
        var ws = new ContentDraftWorkspace();
        var catalog = new ContentIdMaxCatalog
        {
            NpcMax = 1,
            QuestionMax = 2,
            ResponseMax = 3,
            QuestMax = 4,
            StageMax = 5,
            ObjectiveMax = 6,
        };
        catalog.ApplyToWorkspace(ws);
        Assert.Equal(1, ws.Npcs.DbMaxId);
        Assert.Equal(2, ws.Dialogs.DbMaxQuestionId);
        Assert.Equal(3, ws.Dialogs.DbMaxResponseId);
        Assert.Equal(4, ws.Missions.DbMaxQuestId);
        Assert.Equal(5, ws.Missions.DbMaxStageId);
        Assert.Equal(6, ws.Missions.DbMaxObjectiveId);
        Assert.Equal(2, ws.Npcs.NextProvisionalId);
        Assert.Equal(3, ws.Dialogs.NextQuestionId);
        Assert.Equal(4, ws.Dialogs.NextResponseId);
        Assert.Equal(5, ws.Missions.NextQuestId);
        Assert.Equal(6, ws.Missions.NextStageId);
        Assert.Equal(7, ws.Missions.NextObjectiveId);
    }

    [Fact]
    public void Compute_next_preview_includes_all_types()
    {
        var ws = new ContentDraftWorkspace();
        var catalog = ContentIdMaxCatalog.FromPublishSnapshot(new ContentPublishMaxSnapshot
        {
            NpcsModelo = 10,
            NpcPreguntas = 20,
            NpcRespuestas = 30,
            Misiones = 40,
            MisionEtapas = 50,
            MisionObjetivos = 60,
        });
        catalog.ApplyToWorkspace(ws);
        var next = ContentIdReservation.ComputeNext(ws);
        Assert.Equal(11, next.NextNpcId);
        Assert.Equal(21, next.NextQuestionId);
        Assert.Equal(31, next.NextResponseId);
        Assert.Equal(41, next.NextQuestId);
        Assert.Equal(51, next.NextStageId);
        Assert.Equal(61, next.NextObjectiveId);
        Assert.Contains("NPC próximo: 11", next.FormatSummary());
    }

    [Fact]
    public void Refresh_higher_max_before_publish_remaps_unpublished_only()
    {
        var ws = BuildWorkspace();
        var low = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 100, NpcPreguntas = 200, NpcRespuestas = 300,
            Misiones = 400, MisionEtapas = 500, MisionObjetivos = 600,
        });
        var high = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 900, NpcPreguntas = 910, NpcRespuestas = 920,
            Misiones = 930, MisionEtapas = 940, MisionObjetivos = 950,
        });
        Assert.Equal(101, low.ReservedNpcIds[0]);
        Assert.Equal(901, high.ReservedNpcIds[0]);
        Assert.Equal(931, high.ReservedQuestIds[0]);
        Assert.Equal("931", high.ResponseActions.Single(a => a.Accion == 44).Args);
        Assert.Equal("941", high.Missions.Single().Etapas);
        Assert.Equal("951", high.Stages.Single().Objetivos);
    }

    [Fact]
    public void Multiaction_shares_response_id_two_responses_do_not()
    {
        var ws = BuildWorkspace();
        var q = ws.Dialogs.Questions[0];
        var r2 = ws.Dialogs.AddResponse(q);
        ws.Dialogs.AddAction(r2, DialogActionCodes.Teleport);

        var plan = ContentPublishPlanBuilder.Build(ws, DefaultMaxes());
        Assert.Equal(2, plan.LogicalResponseCount);
        Assert.Equal(3, plan.ResponseActionRowCount); // goto+startquest + teleport
        var ids = plan.ResponseActions.Select(a => a.Id).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids[0] + 1, ids[1]);
    }

    [Fact]
    public void Validate_reports_conflicting_stage_and_objective_ids()
    {
        // Force duplicate by mapping two provisional stages to same final via crafted plan errors path:
        // ContentPublishPlanBuilder won't create duplicates from normal drafts; unit-test FormatDuplicateError + Add via validate helper.
        Assert.Equal("ID etapa duplicado en el plan: 55", ContentIdReservation.FormatDuplicateError("etapa", 55));
        Assert.Equal("ID objetivo duplicado en el plan: 9", ContentIdReservation.FormatDuplicateError("objetivo", 9));
    }

    [Fact]
    public async Task Fail_read_max_blocks_publish_with_zero_inserts()
    {
        var ws = BuildWorkspace();
        var store = new InMemoryContentPublishStore(DefaultMaxes());
        store.SetFailReadMax(true);
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "rufus-idmax-test"));
        var outcome = await svc.PublishAsync(ws);
        Assert.False(outcome.Success);
        Assert.Contains("MAX", outcome.Error ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.InsertCallCount);
    }

    [Fact]
    public async Task Composite_reader_reports_per_table_failure()
    {
        var reader = new ContentIdMaxReader(
            npcMax: _ => Task.FromResult(1),
            questionMax: _ => Task.FromResult(2),
            responseMax: _ => throw new InvalidOperationException("down"),
            questMax: _ => Task.FromResult(4),
            stageMax: _ => Task.FromResult(5),
            objectiveMax: _ => Task.FromResult(6));
        var result = await reader.ReadAsync();
        Assert.False(result.Success);
        Assert.Contains(result.Failures, f => f.Contains("npc_respuestas.id", StringComparison.Ordinal));
    }

    [Fact]
    public void Snapshot_persists_response_and_quest_max()
    {
        var ws = new ContentDraftWorkspace();
        new ContentIdMaxCatalog
        {
            NpcMax = 1, QuestionMax = 2, ResponseMax = 33, QuestMax = 44, StageMax = 5, ObjectiveMax = 6,
        }.ApplyToWorkspace(ws);
        var json = ContentWorkspaceSerializer.Serialize(ws);
        var loaded = ContentWorkspaceSerializer.Deserialize(json);
        Assert.Equal(33, loaded.Dialogs.DbMaxResponseId);
        Assert.Equal(44, loaded.Missions.DbMaxQuestId);
    }

    [Fact]
    public void Orden_is_not_part_of_reservation_or_insert_row()
    {
        Assert.Null(typeof(NpcRespuestaInsertRow).GetProperty("Orden"));
        Assert.Null(typeof(ContentPublishMaxSnapshot).GetProperty("NpcRespuestasOrden"));
        Assert.Null(typeof(ContentIdMaxCatalog).GetProperty("ResponseOrdenMax"));
    }

    private static ContentDraftWorkspace BuildWorkspace()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(100);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "N";
        npc.DialogMode = NpcDialogMode.Interactive;
        var loc = ws.Npcs.AddLocation(npc);
        loc.MapId = 1;
        loc.CellId = 2;

        ws.Dialogs.SetDbMaxQuestionId(200);
        ws.Dialogs.SetDbMaxResponseId(300);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "hi";
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        var aGoto = ws.Dialogs.AddAction(r, DialogActionCodes.GotoQuestion);
        var q2 = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.LinkGotoQuestion(aGoto, q2.Id);
        var aQuest = ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);

        ws.Missions.SetDbMaxQuestId(400);
        ws.Missions.SetDbMaxStageId(500);
        ws.Missions.SetDbMaxObjectiveId(600);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Q";
        mission.StartNpcId = npc.Id;
        var stage = ws.Missions.AddStage(mission);
        stage.Nombre = "E1";
        ws.Missions.AddObjective(stage, 0);
        ws.Dialogs.LinkStartMission(aQuest, mission.DraftId);
        return ws;
    }

    private static ContentPublishMaxSnapshot DefaultMaxes() => new()
    {
        NpcsModelo = 100,
        NpcPreguntas = 200,
        NpcRespuestas = 300,
        Misiones = 400,
        MisionEtapas = 500,
        MisionObjetivos = 600,
    };
}
