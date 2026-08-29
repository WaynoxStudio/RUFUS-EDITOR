using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONT.6B.1 — Simple dialog UI resolver. No BD/SFTP/Mapas writes.</summary>
public sealed class ContDialog6b1Tests
{
    private static byte[] Seed(int version, params DialogEsAssignment[] entries) =>
        DialogEsSeed.Create(version, entries);

    private static DialogEsAssignment Q(int id, string text) => new()
    {
        Space = DialogEsSpace.Question,
        Id = id,
        Text = text,
    };

    [Fact]
    public void New_simple_text_without_manual_id_is_pending_not_invalid_id()
    {
        var npc = NpcsModeloDraft.CreateWithDefaults(20062);
        npc.SimpleDialogTextLocal = "Hola buenas como estas?";
        Assert.True(npc.IsPendingDialogEs);
        Assert.Equal(0, npc.Pregunta);
        Assert.False(npc.IsSimpleDialogComplete);
    }

    [Fact]
    public void Existing_id_remains_optional_and_completes_simple_mode()
    {
        var npc = NpcsModeloDraft.CreateWithDefaults(20062);
        npc.SimpleDialogTextLocal = "reuse";
        npc.Pregunta = 1075;
        Assert.False(npc.IsPendingDialogEs);
        Assert.True(npc.IsSimpleDialogComplete);
    }

    [Fact]
    public void Provisional_dq_comes_from_active_swf_not_hardcoded()
    {
        var snap = DialogEsParser.Parse(Seed(1292, Q(20024, "last")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "Hola buenas como estas?";

        var state = DialogEsSimpleUiResolver.ForNpc(ws, npc, snap);
        Assert.True(state.IsPending);
        Assert.Equal(20025, state.ProvisionalDqId);
        Assert.Equal(1292, state.ActiveVersion);
        Assert.Equal(1293, state.TargetVersion);
        var details = state.FormatDetails();
        Assert.Contains("ID D.q provisional: 20025", details, StringComparison.Ordinal);
        Assert.Contains("dialog_es activo: 1292", details, StringComparison.Ordinal);
        Assert.Contains("Versión local prevista: 1293", details, StringComparison.Ordinal);
        Assert.Equal("⚠ Pendiente de publicación dialog_es", state.BannerTitle);
    }

    [Fact]
    public void Provisional_dq_follows_real_max_question_id()
    {
        var snap = DialogEsParser.Parse(Seed(40, Q(7, "x")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(1);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "nuevo";
        var state = DialogEsSimpleUiResolver.ForNpc(ws, npc, snap);
        Assert.Equal(8, state.ProvisionalDqId);
        Assert.NotEqual(20025, state.ProvisionalDqId);
    }

    [Fact]
    public void Batch_pending_simples_get_distinct_provisional_ids()
    {
        var snap = DialogEsParser.Parse(Seed(10, Q(3, "a")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(10);
        var a = ws.Npcs.CreateNew();
        a.SimpleDialogTextLocal = "uno";
        var b = ws.Npcs.CreateNew();
        b.SimpleDialogTextLocal = "dos";
        Assert.Equal(4, DialogEsSimpleUiResolver.ForNpc(ws, a, snap).ProvisionalDqId);
        Assert.Equal(5, DialogEsSimpleUiResolver.ForNpc(ws, b, snap).ProvisionalDqId);
    }

    [Fact]
    public void Pending_simple_still_blocks_bd_plan()
    {
        var snap = DialogEsParser.Parse(Seed(1292, Q(20024, "last")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.SimpleDialogTextLocal = "Hola buenas como estas?";
        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20061,
            NpcPreguntas = 20023,
            NpcRespuestas = 90001,
            Misiones = 1,
            MisionEtapas = 1,
            MisionObjetivos = 1,
        }, snap);
        Assert.False(plan.IsValid);
        Assert.True(plan.HasPendingSimpleDialogEs);
        Assert.Contains(plan.Errors, e => e.Contains("pendiente de publicación dialog_es", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Errors, e => e.Contains("sin ID válido", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Errors, e => e.Contains("asigna un ID existente", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(plan.Questions);
        Assert.Equal(0, plan.LogicalResponseCount);
    }

    [Fact]
    public async Task Resolver_and_plan_do_not_write_bd()
    {
        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 1,
            NpcPreguntas = 1,
            NpcRespuestas = 1,
            Misiones = 1,
            MisionEtapas = 1,
            MisionObjetivos = 1,
        };
        var store = new InMemoryContentPublishStore(maxes);
        var snap = DialogEsParser.Parse(Seed(2, Q(1, "a")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(1);
        var npc = ws.Npcs.CreateNew();
        npc.SimpleDialogTextLocal = "x";
        _ = DialogEsSimpleUiResolver.ForNpc(ws, npc, snap);
        var plan = ContentPublishPlanBuilder.Build(ws, maxes, snap);
        Assert.False(plan.IsValid);
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "j6b1-" + Guid.NewGuid().ToString("N")));
        var outcome = await svc.PublishAsync(ws);
        Assert.False(outcome.Success);
        Assert.Equal(0, store.InsertCallCount);
    }
}
