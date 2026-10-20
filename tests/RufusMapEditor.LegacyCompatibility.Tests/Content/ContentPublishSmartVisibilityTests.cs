using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONTENT.PUBLISH.SMART_VISIBILITY.1</summary>
public sealed class ContentPublishSmartVisibilityTests
{
    private static ContentPublishMaxSnapshot Maxes() => new()
    {
        NpcsModelo = 100,
        NpcPreguntas = 200,
        NpcRespuestas = 300,
        Misiones = 400,
        MisionEtapas = 500,
        MisionObjetivos = 600,
    };

    [Fact]
    public void Sin_cambios_ningun_boton_ambos_al_dia()
    {
        var ws = new ContentDraftWorkspace();
        var state = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), questsEs: QuestsEsParser.Parse(QuestsEsSeed.Create(10)), contentMaxReady: true,
            tryCachedQuestsEs: false);

        Assert.False(state.ShowBdButton);
        Assert.False(state.ShowClientButton);
        Assert.Equal(ContentPublishHeaderEvaluator.BdUpToDate, state.BdStatusLabel);
        Assert.Equal(ContentPublishHeaderEvaluator.ClientUpToDate, state.ClientStatusLabel);
    }

    [Fact]
    public void Solo_BD_pendiente_update_puedeRepetirse()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.QuestsEsPublished = true;
        mission.Nombre = "PRUEBA";
        mission.PuedeRepetirse = true;
        mission.HasPendingBdChanges = true;
        mission.Stages.Add(new MissionStageDraft { Id = 5501, Nombre = "Prueba" });
        mission.MarkPublishedSnapshot();
        mission.HasPendingBdChanges = true;
        mission.PuedeRepetirse = true;

        var vanilla = QuestsEsSeed.Create(40, new[]
        {
            new QuestsEsMissionAssignment
            {
                Quest = new QuestsEsQuestAssignment
                {
                    QuestId = 100004,
                    Name = "PRUEBA",
                    StageIds = new[] { 5501 },
                },
                Stages =
                [
                    new QuestsEsStageAssignment
                    {
                        StageId = 5501,
                        Name = "Prueba",
                        Description = "d",
                        Rewards = new MissionRewardsDraft(),
                    },
                ],
                Objectives = Array.Empty<QuestsEsObjectiveAssignment>(),
            },
        });
        var snap = QuestsEsParser.Parse(vanilla);

        var state = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), snap, contentMaxReady: true, tryCachedQuestsEs: false);

        Assert.True(state.ShowBdButton);
        Assert.True(state.EnableBdButton);
        Assert.Equal(ContentPublishHeaderEvaluator.BdPending, state.BdStatusLabel);
        // Client button may also appear if quests_es repair is detected for the same draft;
        // BD path is what this case asserts.
    }

    [Fact]
    public void Dialogo_simple_pendiente_no_incluye_quests_es()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "Hola";
        Assert.True(npc.IsPendingDialogEs);

        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.QuestsEsPublished = false;
        mission.Stages.Add(new MissionStageDraft { Id = 1, Nombre = "E" });
        Assert.True(QuestsEsPublishBatchBuilder.IsPendingQuestsEs(mission));

        Assert.True(ContentClientRemotePublishService.HasPendingSimpleDialogEs(ws));
        Assert.False(ContentClientRemotePublishService.HasPendingQuestsEs(ws));
    }

    [Fact]
    public void Solo_reparacion_SWF_quests_no_marca_cliente_pendiente()
    {
        var legacy = BuildLegacyPruebaSwf(40);
        var snap = QuestsEsParser.Parse(legacy);
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.QuestsEsPublished = true;
        mission.QuestsEsPublishedVersion = 40;
        mission.HasPendingBdChanges = false;
        mission.Nombre = "PRUEBA";
        mission.Stages.Add(new MissionStageDraft
        {
            Id = 5501,
            Nombre = "Prueba",
            Descripcion = "Descubre el mapa.",
            Rewards = new MissionRewardsDraft { Exp = 200000, Kamas = 200000 },
        });
        mission.Stages[0].Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4661,
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "7411",
        });
        mission.MarkPublishedSnapshot();

        var state = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), snap, contentMaxReady: true, tryCachedQuestsEs: false);

        // Leftover published missions must not force quests_es / Cliente pendiente.
        Assert.False(state.ShowBdButton);
        Assert.False(state.ShowClientButton);
        Assert.Equal(ContentPublishHeaderEvaluator.BdUpToDate, state.BdStatusLabel);
        Assert.Equal(ContentPublishHeaderEvaluator.ClientUpToDate, state.ClientStatusLabel);
    }

    [Fact]
    public void Ambos_pendientes()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "TestNpc";
        npc.PublishedBd = true;
        npc.NpcEsPublished = false;

        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 77;
        mission.HasPendingBdChanges = true;
        mission.Nombre = "M";
        mission.Stages.Add(new MissionStageDraft { Id = 1, Nombre = "E" });
        mission.MarkPublishedSnapshot();
        mission.HasPendingBdChanges = true;

        var state = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), QuestsEsParser.Parse(QuestsEsSeed.Create(5)), contentMaxReady: true,
            tryCachedQuestsEs: false);

        Assert.True(state.ShowBdButton);
        Assert.True(state.ShowClientButton);
    }

    [Fact]
    public void Publicar_BD_quita_boton_BD()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 9;
        mission.HasPendingBdChanges = true;
        mission.Nombre = "X";
        mission.Stages.Add(new MissionStageDraft { Id = 1, Nombre = "E" });
        mission.MarkPublishedSnapshot();
        mission.HasPendingBdChanges = true;

        var before = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), contentMaxReady: true, tryCachedQuestsEs: false);
        Assert.True(before.ShowBdButton);

        mission.HasPendingBdChanges = false;
        var after = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), QuestsEsParser.Parse(QuestsEsSeed.Create(3)), contentMaxReady: true,
            tryCachedQuestsEs: false);
        Assert.False(after.ShowBdButton);
        Assert.Equal(ContentPublishHeaderEvaluator.BdUpToDate, after.BdStatusLabel);
    }

    [Fact]
    public void Publicar_cliente_quita_boton_cliente_tras_primera_escritura_quests()
    {
        var assignment = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment
            {
                QuestId = 100004,
                Name = "PRUEBA",
                StageIds = new[] { 5501 },
            },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 5501,
                    Name = "Prueba",
                    Description = "Descubre el mapa.",
                    Rewards = new MissionRewardsDraft { Exp = 200000, Kamas = 200000 },
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4661,
                    Type = MissionObjectiveTypes.DiscoverMap,
                    Args = "7411",
                    Detalle = "",
                },
            ],
        };
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(40, new[] { assignment }));
        var ws = PublishedPrueba(assignment);
        ws.Missions.Missions[0].QuestsEsPublished = false;
        Assert.True(QuestsEsPublishBatchBuilder.IsPendingQuestsEs(ws.Missions.Missions[0]));

        var before = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), snap, contentMaxReady: true, tryCachedQuestsEs: false);
        Assert.True(before.ShowClientButton);
        Assert.Equal(ContentPublishHeaderEvaluator.ClientPending, before.ClientStatusLabel);

        ws.Missions.Missions[0].QuestsEsPublished = true;
        var after = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), snap, contentMaxReady: true, tryCachedQuestsEs: false);
        Assert.False(after.ShowClientButton);
        Assert.Equal(ContentPublishHeaderEvaluator.ClientUpToDate, after.ClientStatusLabel);
    }

    [Fact]
    public void Preflight_bloqueado_warning_sin_boton_habilitado()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "Bloqueado";
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "Texto nuevo";
        Assert.True(npc.IsPendingDialogEs);

        var state = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), contentMaxReady: true, tryCachedQuestsEs: false);

        Assert.True(state.ShowBdButton);
        Assert.False(state.EnableBdButton);
        Assert.Equal(ContentPublishHeaderTone.Warning, state.BdTone);
        Assert.Equal(ContentPublishHeaderEvaluator.BdWaitingClient, state.BdStatusLabel);
        Assert.True(state.ShowClientButton);
        Assert.True(state.EnableClientButton);
    }

    [Fact]
    public void Sin_MAX_listo_BD_con_trabajo_muestra_warning()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 1;
        mission.HasPendingBdChanges = true;
        mission.Nombre = "X";
        mission.Stages.Add(new MissionStageDraft { Id = 1, Nombre = "E" });
        mission.MarkPublishedSnapshot();
        mission.HasPendingBdChanges = true;

        var state = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), contentMaxReady: false, tryCachedQuestsEs: false);

        Assert.True(state.ShowBdButton);
        Assert.False(state.EnableBdButton);
        Assert.Equal(ContentPublishHeaderEvaluator.BdWarning, state.BdStatusLabel);
    }

    [Fact]
    public void Refresh_recalcula_sin_reiniciar()
    {
        var ws = new ContentDraftWorkspace();
        var s1 = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), QuestsEsParser.Parse(QuestsEsSeed.Create(2)), contentMaxReady: true,
            tryCachedQuestsEs: false);
        Assert.False(s1.ShowBdButton);

        var m = ws.Missions.CreateMission();
        m.PublishedBd = true;
        m.PublishedQuestId = 2;
        m.HasPendingBdChanges = true;
        m.Nombre = "N";
        m.Stages.Add(new MissionStageDraft { Id = 3, Nombre = "E" });
        m.MarkPublishedSnapshot();
        m.HasPendingBdChanges = true;

        var s2 = ContentPublishHeaderEvaluator.Evaluate(
            ws, Maxes(), QuestsEsParser.Parse(QuestsEsSeed.Create(2)), contentMaxReady: true,
            tryCachedQuestsEs: false);
        Assert.True(s2.ShowBdButton);
        Assert.NotEqual(s1.BdStatusLabel, s2.BdStatusLabel);
    }

    private static ContentDraftWorkspace PublishedPrueba(QuestsEsMissionAssignment a)
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = a.Quest.QuestId;
        mission.Nombre = a.Quest.Name;
        mission.Stages.Add(new MissionStageDraft
        {
            Id = a.Stages[0].StageId,
            Nombre = a.Stages[0].Name,
            Descripcion = a.Stages[0].Description,
            Rewards = a.Stages[0].Rewards.Clone(),
        });
        if (a.Objectives.Count > 0)
        {
            mission.Stages[0].Objectives.Add(new MissionObjectiveDraft
            {
                Id = a.Objectives[0].ObjectiveId,
                Tipo = a.Objectives[0].Type,
                Args = a.Objectives[0].Args,
                Detalle = a.Objectives[0].Detalle,
            });
        }

        mission.MarkPublishedSnapshot();
        return ws;
    }

    private static QuestsEsSnapshot BuildLegacySnap() =>
        QuestsEsParser.Parse(BuildLegacyPruebaSwf(40));

    private static byte[] BuildLegacyPruebaSwf(int version)
    {
        var empty = QuestsEsSeed.Create(version - 1);
        var parsed = QuestsEsParser.ParseInternal(empty);
        var assignment = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment
            {
                QuestId = 100004,
                Name = "PRUEBA",
                StageIds = new[] { 5501 },
            },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 5501,
                    Name = "Prueba",
                    Description = "Descubre el mapa.",
                    Rewards = new MissionRewardsDraft { Exp = 200000, Kamas = 200000 },
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4661,
                    Type = MissionObjectiveTypes.DiscoverMap,
                    Args = "7411",
                    Detalle = "",
                },
            ],
        };
        var action = QuestsEsMutator.ApplyWithLegacyQuest(parsed, version, assignment);
        return parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, action).Write(compress: true);
    }
}
