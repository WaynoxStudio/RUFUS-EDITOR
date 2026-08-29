using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONT-DIALOG.3 — Simple vs Interactive dialog modes.</summary>
public sealed class ContDialog3Tests
{
    private static ContentPublishMaxSnapshot Maxes() => new()
    {
        NpcsModelo = 20061,
        NpcPreguntas = 20023,
        NpcRespuestas = 90001,
        Misiones = 100003,
        MisionEtapas = 5500,
        MisionObjetivos = 4214,
    };

    [Fact]
    public void New_npc_defaults_to_simple_mode()
    {
        var npc = NpcsModeloDraft.CreateWithDefaults(20062);
        Assert.Equal(NpcDialogMode.Simple, npc.DialogMode);
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        Assert.Equal(NpcDialogMode.Simple, batch.CreateNew().DialogMode);
    }

    [Fact]
    public void Simple_plan_has_zero_preguntas_and_respuestas_and_keeps_pregunta_id()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        Assert.Equal(NpcDialogMode.Simple, npc.DialogMode);
        npc.Nombre = "Guerrero Haco Norss";
        npc.SimpleDialogTextLocal = "¡Por Astrub!";
        npc.Pregunta = 1075;

        // Leftover interactive drafts must NOT be published in Simple mode.
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var orphan = ws.Dialogs.CreateQuestion(npc.Id);
        orphan.TextLocal = "should not publish";
        var r = ws.Dialogs.AddResponse(orphan);
        ws.Dialogs.AddAction(r, DialogActionCodes.Teleport);

        var plan = ContentPublishPlanBuilder.Build(ws, Maxes());
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Single(plan.Npcs);
        Assert.Equal(1075, plan.Npcs[0].Pregunta);
        Assert.Empty(plan.Questions);
        Assert.Empty(plan.ResponseActions);
        Assert.Empty(plan.ReservedQuestionIds);
        Assert.Empty(plan.ReservedResponseIds);
        Assert.Equal(0, plan.LogicalResponseCount);
    }

    [Fact]
    public void Simple_preview_counts_match_zero_dialog_tables()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.Pregunta = 1075;

        var plan = ContentPublishPlanBuilder.Build(ws, Maxes());
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Single(plan.Npcs);
        Assert.Empty(plan.Questions);
        Assert.Equal(0, plan.LogicalResponseCount);
        Assert.Equal(0, plan.ResponseActionRowCount);
    }

    [Fact]
    public void Simple_text_without_id_blocks_publish_plan()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "Texto nuevo sin ID";
        npc.Pregunta = 0;

        Assert.True(npc.IsPendingDialogEs);
        Assert.True(npc.IsSimpleDialogBlocked);

        var plan = ContentPublishPlanBuilder.Build(ws, Maxes());
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains("dialog_es", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Interactive_still_publishes_preguntas_and_respuestas()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        ws.Dialogs.AddAction(r, DialogActionCodes.Teleport).Args = "1,2";

        var plan = ContentPublishPlanBuilder.Build(ws, Maxes());
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Single(plan.Questions);
        Assert.Equal(1, plan.LogicalResponseCount);
        Assert.Single(plan.ResponseActions);
        Assert.Equal(20024, plan.Npcs[0].Pregunta);
    }

    [Fact]
    public void Workspace_roundtrip_keeps_dialog_mode_and_simple_text()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "Hola";
        npc.Pregunta = 1075;

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        Assert.Equal(NpcDialogMode.Simple, loaded.Npcs.Drafts[0].DialogMode);
        Assert.Equal("Hola", loaded.Npcs.Drafts[0].SimpleDialogTextLocal);
        Assert.Equal(1075, loaded.Npcs.Drafts[0].Pregunta);
    }

    [Fact]
    public async Task Simple_publish_writes_npc_pregunta_only()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.Nombre = "Haco";
        npc.Pregunta = 1075;

        var store = new InMemoryContentPublishStore(Maxes());
        var journal = Path.Combine(Path.GetTempPath(), "rufus-dialog3-" + Guid.NewGuid().ToString("N"));
        var svc = new ContentPublishService(store, journal);
        var outcome = await svc.PublishAsync(ws);
        Assert.True(outcome.Success, outcome.Error);
        Assert.Single(store.Npcs);
        Assert.Equal(1075, store.Npcs.Values.Single().Pregunta);
        Assert.Empty(store.Questions);
        Assert.Empty(store.Responses);
        Assert.Equal(1075, ws.Npcs.Drafts[0].Pregunta);
        Assert.True(ws.Npcs.Drafts[0].PublishedBd);
    }

    [Fact]
    public void RemoveQuestionsForNpc_clears_interactive_tree()
    {
        var batch = new DialogDraftBatch();
        batch.SetDbMaxQuestionId(20023);
        var q = batch.CreateQuestion(20062);
        batch.AddResponse(q);
        Assert.Equal(1, batch.RemoveQuestionsForNpc(20062));
        Assert.Empty(batch.QuestionsForNpc(20062));
    }

    [Fact]
    public void Legacy_json_without_dialogMode_loads_as_interactive()
    {
        // Enum Interactive = 0 → missing camelCase dialogMode deserializes to Interactive.
        var snap = new ContentWorkspaceSnapshot
        {
            NpcDbMaxId = 20061,
            Npcs =
            {
                new NpcsModeloDraft
                {
                    Id = 20062,
                    Nombre = "Legacy",
                    // DialogMode left at default Interactive=0
                },
            },
        };
        var json = System.Text.Json.JsonSerializer.Serialize(snap, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        });
        // Strip dialogMode if serializer wrote it — force absence
        json = json.Replace("\"dialogMode\":0,", "").Replace(",\"dialogMode\":0", "");
        var loaded = ContentWorkspaceSerializer.Deserialize(json);
        Assert.Equal(NpcDialogMode.Interactive, loaded.Npcs.Drafts[0].DialogMode);
    }
}
