using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionMapDeleteAndMonsterIdInputTests
{
    public MissionMapDeleteAndMonsterIdInputTests()
    {
        MobsModeloNameCache.Shared = null;
        ObjetosModeloNameCache.Shared = null;
    }

    [Fact]
    public void X_mapa_publicado_elimina_del_draft()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        m.Nombre = "M";
        m.PublishedBd = true;
        m.PublishedQuestId = 100;
        m.HasPendingBdChanges = true;
        var stage = ws.Missions.AddStage(m);
        m.PublishedStageIds.Add(stage.Id);
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Args = MissionObjectiveArgsCodec.BuildDiscoverMap(10306);
        m.PublishedObjectiveIds.Add(map.Id);
        var mob = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DefeatMobs);
        mob.Args = MissionObjectiveArgsCodec.BuildDefeatMobs([(36, 5)], null, null);
        m.PublishedObjectiveIds.Add(mob.Id);

        Assert.True(ws.Missions.RemoveObjective(m, stage, map));
        Assert.Single(stage.Objectives);
        Assert.Equal(mob.Id, stage.Objectives[0].Id);
        Assert.Contains(map.Id, m.PendingDeletedObjectiveIds);
    }

    [Fact]
    public void X_mapa_elimina_realmente_del_draft()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        m.Nombre = "M";
        var stage = ws.Missions.AddStage(m);
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Args = MissionObjectiveArgsCodec.BuildDiscoverMap(7411);
        var mob = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DefeatMobs);
        mob.Args = MissionObjectiveArgsCodec.BuildDefeatMobs([(36, 5)], null, null);
        Assert.Equal(2, stage.Objectives.Count);

        Assert.True(ws.Missions.RemoveObjective(m, stage, map));
        Assert.Single(stage.Objectives);
        Assert.Equal(mob.Id, stage.Objectives[0].Id);
        Assert.DoesNotContain(stage.Objectives, o => o.Tipo == MissionObjectiveTypes.DiscoverMap);
    }

    [Theory]
    [InlineData(MissionObjectiveTypes.DefeatMobs)]
    [InlineData(MissionObjectiveTypes.ShowItemToNpc)]
    [InlineData(MissionObjectiveTypes.DeliverItemsToNpc)]
    [InlineData(MissionObjectiveTypes.TalkToNpc)]
    [InlineData(MissionObjectiveTypes.UseItem)]
    public void X_elimina_otros_tipos(int tipo)
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        var stage = ws.Missions.AddStage(m);
        var o = ws.Missions.AddObjective(stage, tipo);
        o.Args = tipo switch
        {
            MissionObjectiveTypes.DefeatMobs => MissionObjectiveArgsCodec.BuildDefeatMobs([(1, 1)], null, null),
            MissionObjectiveTypes.ShowItemToNpc or MissionObjectiveTypes.DeliverItemsToNpc =>
                MissionObjectiveArgsCodec.BuildShowOrDeliver(1, 2, 1, null, null),
            MissionObjectiveTypes.TalkToNpc => MissionObjectiveArgsCodec.BuildTalk(1, null, null),
            MissionObjectiveTypes.UseItem => MissionObjectiveArgsCodec.BuildUseItem(5),
            _ => "",
        };
        Assert.True(ws.Missions.RemoveObjective(m, stage, o));
        Assert.Empty(stage.Objectives);
    }

    [Fact]
    public void Numeracion_se_actualiza_tras_eliminar_mapa()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        var stage = ws.Missions.AddStage(m);
        var a = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        a.Args = "100";
        var b = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DefeatMobs);
        b.Args = "[36,2]";
        var c = ws.Missions.AddObjective(stage, MissionObjectiveTypes.TalkToNpc);
        c.Args = "[1]";
        Assert.True(ws.Missions.RemoveObjective(m, stage, a));
        var remaining = stage.Objectives.Where(o => !o.IsAutoReturnToStartNpc).ToList();
        Assert.Equal(2, remaining.Count);
        Assert.Equal(b.Id, remaining[0].Id);
        Assert.Equal(c.Id, remaining[1].Id);
    }

    [Fact]
    public void Objetivo_mapa_eliminado_no_entra_en_plan()
    {
        var ws = BuildMissionWithNpc();
        var m = ws.Missions.Missions[0];
        var stage = m.Stages[0];
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Args = "7411";
        Assert.True(ws.Missions.RemoveObjective(m, stage, map));

        var plan = ContentPublishPlanBuilder.Build(ws, DefaultMaxes(ws), itemIdExistsInObjetosModelo: _ => true);
        Assert.DoesNotContain(plan.Objectives, o => o.Tipo == MissionObjectiveTypes.DiscoverMap);
    }

    [Fact]
    public void Varios_monstruos_serializacion_y_parse()
    {
        var args = MissionObjectiveArgsCodec.BuildDefeatMobs([(36, 5), (123, 2)], null, null);
        Assert.Equal("[36,5,123,2]", args);
        var pairs = MissionObjectiveArgsCodec.ParseDefeatMobPairs(args);
        Assert.Equal(2, pairs.Count);
        Assert.Equal((36, 5), pairs[0]);
        Assert.Equal((123, 2), pairs[1]);
    }

    [Fact]
    public void UiSync_varios_monstruos()
    {
        var d = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.DefeatMobs };
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DefeatMobs,
            MobPairs = [(36, 5), (123, 2)],
        }));
        Assert.Equal("[36,5,123,2]", d.Args);
    }

    [Fact]
    public void Monster_id_manual_serializacion_identica()
    {
        var d = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.DefeatMobs };
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DefeatMobs,
            MobId = "123",
            Qty = "5",
        }));
        Assert.Equal("[123,5]", d.Args);
        Assert.Equal(
            MissionObjectiveArgsCodec.BuildDefeatMobs([(123, 5)], null, null),
            d.Args);
    }

    [Fact]
    public void Cache_mob_existente_y_inexistente()
    {
        var cache = new MobsModeloNameCache(id =>
            Task.FromResult(id == 123 ? "Crujidor" : null));
        Assert.Equal("Crujidor", cache.ResolveSync(123));
        Assert.True(cache.Exists(123));
        Assert.Null(cache.ResolveSync(999));
        Assert.False(cache.Exists(999));
        Assert.Equal("Monstruo no encontrado.", MobsModeloNameCache.NotFoundMessage);
    }

    [Fact]
    public void Cambiar_id_actualiza_nombre_en_cache()
    {
        var cache = new MobsModeloNameCache(id => Task.FromResult(id switch
        {
            36 => "Bouftou",
            123 => "Crujidor",
            _ => null,
        }));
        Assert.Equal("Bouftou", cache.ResolveSync(36));
        Assert.Equal("Crujidor", cache.ResolveSync(123));
    }

    [Fact]
    public void Cantidad_se_conserva()
    {
        Assert.True(MissionObjectiveArgsCodec.TryGetDefeatMobPair("[36,7]", out var mob, out var qty));
        Assert.Equal(36, mob);
        Assert.Equal(7, qty);
    }

    [Fact]
    public void Draft_antiguo_carga_id_cantidad()
    {
        var args = MissionObjectiveArgsCodec.BuildDefeatMobs([(36, 5)], 1, -2);
        Assert.True(MissionObjectiveArgsCodec.TryGetDefeatMobPair(args, out var mob, out var qty));
        Assert.Equal(36, mob);
        Assert.Equal(5, qty);
    }

    [Fact]
    public void Publish_bloqueado_si_mob_inexistente()
    {
        var ws = BuildMissionWithDefeatMob(999001);
        var plan = ContentPublishPlanBuilder.Build(
            ws,
            DefaultMaxes(ws),
            mobIdExistsInMobsModelo: _ => false);
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains(MobsModeloNameCache.NotFoundMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void Publish_ok_si_mob_existe_sin_buscar()
    {
        var ws = BuildMissionWithDefeatMob(36);
        var plan = ContentPublishPlanBuilder.Build(
            ws,
            DefaultMaxes(ws),
            mobIdExistsInMobsModelo: id => id == 36);
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        var row = Assert.Single(plan.Objectives, o => o.Tipo == MissionObjectiveTypes.DefeatMobs);
        Assert.True(MissionObjectiveArgsCodec.TryGetDefeatMobPair(row.Args, out var mob, out var qty));
        Assert.Equal(36, mob);
        Assert.Equal(5, qty);
    }

    [Fact]
    public void Columnas_mobs_modelo_confirmadas()
    {
        Assert.Equal("mobs_modelo", MobsModeloNameColumns.DefaultTable);
        Assert.Equal("id", MobsModeloNameColumns.Id);
        Assert.Equal("nombre", MobsModeloNameColumns.Nombre);
    }

    private static ContentDraftWorkspace BuildMissionWithNpc()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20000);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "N";
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(1);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "Q";
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        var a = ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        m.Nombre = "M";
        m.StartNpcId = npc.Id;
        m.PregDarPreguntaId = q.Id;
        var stage = ws.Missions.AddStage(m);
        stage.Nombre = "E1";
        ws.Dialogs.LinkStartMission(a, m.DraftId);
        return ws;
    }

    private static ContentDraftWorkspace BuildMissionWithDefeatMob(int mobId)
    {
        var ws = BuildMissionWithNpc();
        var o = ws.Missions.AddObjective(ws.Missions.Missions[0].Stages[0], MissionObjectiveTypes.DefeatMobs);
        o.Args = MissionObjectiveArgsCodec.BuildDefeatMobs([(mobId, 5)], null, null);
        return ws;
    }

    private static ContentPublishMaxSnapshot DefaultMaxes(ContentDraftWorkspace ws) => new()
    {
        NpcsModelo = ws.Npcs.DbMaxId,
        NpcPreguntas = ws.Dialogs.DbMaxQuestionId,
        NpcRespuestas = Math.Max(1, ws.Dialogs.DbMaxResponseId),
        Misiones = ws.Missions.DbMaxQuestId,
        MisionEtapas = ws.Missions.DbMaxStageId,
        MisionObjetivos = ws.Missions.DbMaxObjectiveId,
    };
}
