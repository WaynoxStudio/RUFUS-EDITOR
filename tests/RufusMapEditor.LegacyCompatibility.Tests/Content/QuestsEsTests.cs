using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class QuestsEsTests
{
    private static string CachedRealSwfPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "lang-cache",
            "quests_es_1275.swf");

    [Fact]
    public void Parse_real_cached_swf_when_present()
    {
        if (!File.Exists(CachedRealSwfPath))
            return;

        var bytes = File.ReadAllBytes(CachedRealSwfPath);
        var snap = QuestsEsParser.Parse(bytes);
        Assert.Equal(1275, snap.Version);
        Assert.Equal(6, snap.SwfVersion);
        Assert.True(snap.WasCompressed);
        Assert.True(snap.HasFileEnd);
        Assert.True(snap.Quests.Count > 0);
        Assert.True(snap.Stages.Count > 0);
        Assert.True(snap.Objectives.Count > 0);
        Assert.True(snap.TypeTemplates.Count > 0);
    }

    [Fact]
    public void Encode_empty_rewards_reverse_mapping()
    {
        var rewards = new MissionRewardsDraft();
        var slots = QuestsEsCodec.EncodeRewards(rewards);
        Assert.Equal(6, slots.Length);
        Assert.True(QuestsEsCodec.IsEmptyRewards(slots));
        Assert.Null(slots[0]);
        Assert.Null(slots[5]);
    }

    [Fact]
    public void Encode_objective_tipo3_reverses_p()
    {
        var obj = MissionObjectiveDraft.CreateDeliverItems(10, 20003, 500, 3);
        var p = QuestsEsCodec.EncodeObjectiveParameters(obj);
        Assert.Equal(new object?[] { 3, 500, 20003 }, p);
    }

    [Fact]
    public void Mutator_preserves_existing_q_count_and_adds_one()
    {
        var existing = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment
            {
                QuestId = 100,
                Name = "Existente",
                StageIds = new[] { 200 },
            },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 200,
                    Name = "Etapa A",
                    Description = "Desc",
                    Rewards = new MissionRewardsDraft(),
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 300,
                    Type = MissionObjectiveTypes.TalkToNpc,
                    Args = "[20003]",
                    Detalle = "",
                },
            ],
        };
        var source = QuestsEsSeed.Create(10, new[] { existing });
        var sourceSnap = QuestsEsParser.Parse(source);
        var addition = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment
            {
                QuestId = 101,
                Name = "Nueva",
                StageIds = new[] { 201 },
            },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 201,
                    Name = "Etapa B",
                    Description = "Otra",
                    Rewards = new MissionRewardsDraft { Exp = 50 },
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 301,
                    Type = MissionObjectiveTypes.Manual,
                    Args = "",
                    Detalle = "Haz algo",
                },
            ],
        };
        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = source,
            Additions = new[] { addition },
        });
        Assert.True(gen.Success, gen.Error);
        var output = QuestsEsParser.Parse(gen.OutputBytes!);
        Assert.Equal(sourceSnap.QuestAssignmentCount + 1, output.QuestAssignmentCount);
        Assert.True(output.Quests.ContainsKey(100));
        Assert.True(output.Quests.ContainsKey(101));
    }

    [Fact]
    public void Batch_detects_id_conflict()
    {
        var seeded = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment { QuestId = 5500, Name = "Ocupada", StageIds = new[] { 5501 } },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 5501,
                    Name = "E",
                    Description = "d",
                    Rewards = new MissionRewardsDraft(),
                },
            ],
            Objectives = [],
        };
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(11, new[] { seeded }));
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 5500;
        mission.Nombre = "Conflicto";
        mission.Stages.Add(new MissionStageDraft
        {
            Id = 99999,
            Nombre = "E",
            Objectives = { new MissionObjectiveDraft { Id = 88888, Tipo = MissionObjectiveTypes.Manual } },
        });

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.False(batch.IsValid);
        Assert.Contains(batch.Errors, e => e.Contains("conflicto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bump_client_quests_token_preserves_dialog_npc_maps()
    {
        var src = "11&f=maps,es,1283|dialog,es,1294|quests,es,1275|npc,es,1289";
        Assert.True(VersionsEsParser.TryBumpContentClientVersions(
            src, null, null, null, null, out var bumped, out var err,
            1275, 1276), err);
        Assert.Contains("quests,es,1276", bumped, StringComparison.Ordinal);
        Assert.Contains("dialog,es,1294", bumped, StringComparison.Ordinal);
        Assert.Contains("npc,es,1289", bumped, StringComparison.Ordinal);
        Assert.Contains("maps,es,1283", bumped, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFromBdPlan_uses_same_ids_as_ContentPublishPlan()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(100);
        var npc = ws.Npcs.CreateNew();
        npc.Id = 101;
        npc.Nombre = "NPC";
        npc.DialogMode = NpcDialogMode.Simple;

        ws.Missions.SetDbMaxQuestId(100003);
        ws.Missions.SetDbMaxStageId(5500);
        ws.Missions.SetDbMaxObjectiveId(4214);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.StartNpcId = npc.Id;
        mission.PublishedBd = false;
        var stage = ws.Missions.AddStage(mission);
        stage.Nombre = "Prueba";
        stage.Descripcion = "Descripción de PRUEBA";
        var obj = ws.Missions.AddObjective(stage);
        obj.Tipo = MissionObjectiveTypes.Manual;

        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 100,
            NpcPreguntas = 1,
            NpcRespuestas = 1,
            Misiones = 100003,
            MisionEtapas = 5500,
            MisionObjetivos = 4214,
        };
        // Simulate client ahead on objectives (real quests_es_1275: MAX Q.o=4660).
        var occupiedObj = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment { QuestId = 900, Name = "x", StageIds = new[] { 901 } },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 901,
                    Name = "s",
                    Description = "d",
                    Rewards = new MissionRewardsDraft(),
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4660,
                    Type = MissionObjectiveTypes.Manual,
                    Args = "",
                    Detalle = "",
                },
            ],
        };
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(1275, new[] { occupiedObj }));
        var plan = ContentPublishPlanBuilder.Build(ws, maxes, questsEs: snap);
        Assert.Equal(100004, plan.QuestIdMap[mission.DraftId]);
        Assert.Equal(5501, plan.StageIdMap[stage.Id]);
        Assert.Equal(4661, plan.ObjectiveIdMap[obj.Id]);

        var batch = QuestsEsPublishBatchBuilder.BuildFromBdPlan(ws, snap, plan);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.True(batch.IsPreviewPendingBd);
        Assert.Equal(1, batch.NewMissionCount);
        Assert.Equal(100004, batch.Additions[0].Quest.QuestId);
        Assert.Equal(5501, batch.Additions[0].Stages[0].StageId);
        Assert.Equal(4661, batch.Additions[0].Objectives[0].ObjectiveId);
        Assert.Equal(100004, mission.PreviewPlannedQuestId);

        var preview = batch.FormatPreview();
        Assert.Contains("PREVISIÓN", preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Q.q[100004]", preview, StringComparison.Ordinal);
        Assert.Contains("Q.s[5501]", preview, StringComparison.Ordinal);
        Assert.Contains("Q.o[4661]", preview, StringComparison.Ordinal);

        Assert.False(QuestsEsPublishBatchBuilder.IsPendingQuestsEs(mission));
        var writeBatch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.False(writeBatch.IsValid);
        Assert.Contains(writeBatch.Errors, e => e.Contains("Publica primero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preview_excludes_orphan_drafts_from_principal_section()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(100);
        var npc = ws.Npcs.CreateNew();
        npc.Id = 101;
        npc.Nombre = "NPC";
        npc.DialogMode = NpcDialogMode.Simple;

        var planned = ws.Missions.CreateMission();
        planned.Nombre = "PRUEBA";
        planned.StartNpcId = npc.Id;
        var stage = ws.Missions.AddStage(planned);
        stage.Nombre = "Prueba";
        var obj = ws.Missions.AddObjective(stage);
        obj.Tipo = MissionObjectiveTypes.Manual;

        var orphanA = ws.Missions.CreateMission();
        orphanA.Nombre = "Misión 20062";
        orphanA.StartNpcId = null;
        ws.Missions.AddStage(orphanA);

        var orphanB = ws.Missions.CreateMission();
        orphanB.Nombre = "Pico Firme";
        orphanB.StartNpcId = 999999;
        var orphanStage = ws.Missions.AddStage(orphanB);
        // Conflicting objective id that would block if orphan entered batch.
        orphanStage.Objectives.Add(new MissionObjectiveDraft
        {
            Id = 1,
            Tipo = MissionObjectiveTypes.Manual,
        });

        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 100,
            Misiones = 100003,
            MisionEtapas = 5500,
            MisionObjetivos = 4660,
        };
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(1275));
        var plan = ContentPublishPlanBuilder.Build(ws, maxes, questsEs: snap);
        Assert.True(plan.QuestIdMap.ContainsKey(planned.DraftId));
        Assert.False(plan.QuestIdMap.ContainsKey(orphanA.DraftId));
        Assert.False(plan.QuestIdMap.ContainsKey(orphanB.DraftId));

        var batch = QuestsEsPublishBatchBuilder.BuildFromBdPlan(ws, snap, plan);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.Equal(1, batch.NewMissionCount);
        Assert.Equal(1, batch.Bindings.Count);
        Assert.DoesNotContain(batch.Bindings, b => b.Kind == "orphan");
        Assert.Equal(2, batch.ExcludedDraftNames.Count);
        Assert.Contains("Misión 20062", batch.ExcludedDraftNames);
        Assert.Contains("Pico Firme", batch.ExcludedDraftNames);
        Assert.Empty(batch.Notes);

        Assert.Single(batch.Additions);
        Assert.Equal(100004, batch.Additions[0].Quest.QuestId);
        Assert.DoesNotContain(batch.Additions, a => a.Quest.Name.Contains("20062", StringComparison.Ordinal));

        var preview = batch.FormatPreview();
        Assert.Contains("Cambios: 1", preview, StringComparison.Ordinal);
        Assert.Contains("Q.q[100004]", preview, StringComparison.Ordinal);
        Assert.Contains("PRUEBA", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Misión 20062", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Pico Firme", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Quest ID: 0", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("BORRADORES EXCLUIDOS", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("no entra en el plan", preview, StringComparison.OrdinalIgnoreCase);

        // Optional secondary section only when explicitly enabled.
        var shown = new QuestsEsPublishBatch
        {
            Bindings = batch.Bindings,
            SourceVersion = batch.SourceVersion,
            TargetVersion = batch.TargetVersion,
            ExcludedDraftNames = batch.ExcludedDraftNames,
            ShowExcludedDraftsInPreview = true,
            IsPreviewPendingBd = true,
        };
        var withExcluded = shown.FormatPreview();
        Assert.Contains("BORRADORES EXCLUIDOS DEL LOTE", withExcluded, StringComparison.Ordinal);
        Assert.Contains("- Misión 20062", withExcluded, StringComparison.Ordinal);
        Assert.DoesNotContain("Quest ID: 0", withExcluded, StringComparison.Ordinal);
        Assert.DoesNotContain("Q.q[0]", withExcluded, StringComparison.Ordinal);
    }

    [Fact]
    public void Namespaces_independent_same_numeric_id_allowed_across_collections()
    {
        var sharedId = 1186;
        var seed = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment
            {
                QuestId = sharedId,
                Name = "Q",
                StageIds = new[] { 10 },
            },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 10,
                    Name = "S",
                    Description = "d",
                    Rewards = new MissionRewardsDraft(),
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = sharedId,
                    Type = MissionObjectiveTypes.Manual,
                    Args = "",
                    Detalle = "",
                },
            ],
        };
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(12, new[] { seed }));
        Assert.True(snap.ContainsQuest(sharedId));
        Assert.True(snap.ContainsObjective(sharedId));
        Assert.False(snap.ContainsStage(sharedId));
    }

    [Fact]
    public void Conflict_reports_specific_collection_q_s_o()
    {
        var seeded = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment { QuestId = 50, Name = "O", StageIds = new[] { 60 } },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 60,
                    Name = "E",
                    Description = "d",
                    Rewards = new MissionRewardsDraft(),
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 70,
                    Type = MissionObjectiveTypes.Manual,
                    Args = "",
                    Detalle = "",
                },
            ],
        };
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(11, new[] { seeded }));

        // Q.q conflict
        {
            var ws = new ContentDraftWorkspace();
            var m = ws.Missions.CreateMission();
            m.PublishedBd = true;
            m.PublishedQuestId = 50;
            m.Nombre = "C";
            m.Stages.Add(new MissionStageDraft
            {
                Id = 999,
                Nombre = "E",
                Objectives = { new MissionObjectiveDraft { Id = 888, Tipo = MissionObjectiveTypes.Manual } },
            });
            var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
            Assert.False(batch.IsValid);
            Assert.Contains(batch.Errors, e => e.Contains("Q.q[50]", StringComparison.Ordinal));
        }

        // Q.s conflict
        {
            var ws = new ContentDraftWorkspace();
            var m = ws.Missions.CreateMission();
            m.PublishedBd = true;
            m.PublishedQuestId = 51;
            m.Nombre = "C";
            m.Stages.Add(new MissionStageDraft
            {
                Id = 60,
                Nombre = "E",
                Objectives = { new MissionObjectiveDraft { Id = 888, Tipo = MissionObjectiveTypes.Manual } },
            });
            var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
            Assert.False(batch.IsValid);
            Assert.Contains(batch.Errors, e => e.Contains("Q.s[60]", StringComparison.Ordinal));
        }

        // Q.o conflict
        {
            var ws = new ContentDraftWorkspace();
            var m = ws.Missions.CreateMission();
            m.PublishedBd = true;
            m.PublishedQuestId = 51;
            m.Nombre = "C";
            m.Stages.Add(new MissionStageDraft
            {
                Id = 61,
                Nombre = "E",
                Objectives = { new MissionObjectiveDraft { Id = 70, Tipo = MissionObjectiveTypes.Manual } },
            });
            var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
            Assert.False(batch.IsValid);
            Assert.Contains(batch.Errors, e => e.Contains("Q.o[70]", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EffectiveContentMax_bd_behind_client_uses_quests_es()
    {
        var bd = new ContentPublishMaxSnapshot
        {
            Misiones = 100003,
            MisionEtapas = 5500,
            MisionObjetivos = 4214,
        };
        var seed = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment { QuestId = 1186, Name = "q", StageIds = new[] { 5500 } },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 5500,
                    Name = "s",
                    Description = "d",
                    Rewards = new MissionRewardsDraft(),
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4660,
                    Type = MissionObjectiveTypes.Manual,
                    Args = "",
                    Detalle = "",
                },
            ],
        };
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(1275, new[] { seed }));
        var eff = ContentIdReservation.EffectiveContentMax(bd, snap);
        Assert.Equal(100003, eff.Misiones);
        Assert.Equal(5500, eff.MisionEtapas);
        Assert.Equal(4660, eff.MisionObjetivos);
    }

    [Fact]
    public void Bd_and_client_plans_share_effective_ids()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(100);
        var npc = ws.Npcs.CreateNew();
        npc.Id = 101;
        npc.Nombre = "NPC";
        npc.DialogMode = NpcDialogMode.Simple;
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.StartNpcId = npc.Id;
        var stage = ws.Missions.AddStage(mission);
        stage.Nombre = "Prueba";
        var obj = ws.Missions.AddObjective(stage);
        obj.Tipo = MissionObjectiveTypes.Manual;

        var bdMax = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 100,
            Misiones = 100003,
            MisionEtapas = 5500,
            MisionObjetivos = 4214,
        };
        var seed = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment { QuestId = 1, Name = "q", StageIds = new[] { 2 } },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 2,
                    Name = "s",
                    Description = "d",
                    Rewards = new MissionRewardsDraft(),
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4660,
                    Type = MissionObjectiveTypes.Manual,
                    Args = "",
                    Detalle = "",
                },
            ],
        };
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(1275, new[] { seed }));
        var bdPlan = ContentPublishPlanBuilder.Build(ws, bdMax, questsEs: snap);
        var clientPlan = ContentPublishPlanBuilder.Build(ws, bdMax, questsEs: snap);
        Assert.Equal(bdPlan.QuestIdMap[mission.DraftId], clientPlan.QuestIdMap[mission.DraftId]);
        Assert.Equal(bdPlan.StageIdMap[stage.Id], clientPlan.StageIdMap[stage.Id]);
        Assert.Equal(bdPlan.ObjectiveIdMap[obj.Id], clientPlan.ObjectiveIdMap[obj.Id]);
        Assert.Equal(100004, bdPlan.QuestIdMap[mission.DraftId]);
        Assert.Equal(5501, bdPlan.StageIdMap[stage.Id]);
        Assert.Equal(4661, bdPlan.ObjectiveIdMap[obj.Id]);
    }

    [Fact]
    public void Build_after_bd_uses_published_ids_and_notes_planned_mismatch()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.PreviewPlannedQuestId = 100099;
        var stage = ws.Missions.AddStage(mission);
        stage.Nombre = "Prueba";
        stage.Id = 5501;
        var obj = ws.Missions.AddObjective(stage);
        obj.Id = 4661;
        obj.Tipo = MissionObjectiveTypes.Manual;

        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(1275));
        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.False(batch.IsPreviewPendingBd);
        Assert.Equal(100004, batch.Additions[0].Quest.QuestId);
        Assert.Equal(5501, batch.Additions[0].Stages[0].StageId);
        Assert.Equal(4661, batch.Additions[0].Objectives[0].ObjectiveId);
        Assert.Contains(batch.Notes, n => n.Contains("100099", StringComparison.Ordinal));
        Assert.Contains("LISTO PARA PUBLICAR", batch.FormatPreview(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Post_bd_orphan_drafts_do_not_force_preview_pending_bd()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20064);
        var npc = ws.Npcs.CreateNew();
        npc.Id = 20065;
        npc.Nombre = "RUFUS";
        npc.PublishedBd = true;
        npc.DialogMode = NpcDialogMode.Interactive;

        var published = ws.Missions.CreateMission();
        published.Nombre = "PRUEBA";
        published.PublishedBd = true;
        published.PublishedQuestId = 100004;
        published.QuestsEsPublished = false;
        published.StartNpcId = npc.Id;
        var st = ws.Missions.AddStage(published);
        st.Id = 5501;
        st.Nombre = "Prueba";
        var ob = ws.Missions.AddObjective(st);
        ob.Id = 4661;
        ob.Tipo = MissionObjectiveTypes.Manual;

        var orphan = ws.Missions.CreateMission();
        orphan.Nombre = "Misión 20062";
        orphan.PublishedBd = false;
        orphan.StartNpcId = null;
        ws.Missions.AddStage(orphan);

        Assert.False(QuestsEsPublishBatchBuilder.HasUnpublishedBdMissions(ws));
        Assert.True(QuestsEsPublishBatchBuilder.IsPendingQuestsEs(published));

        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(1275));
        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.False(batch.IsPreviewPendingBd);
        Assert.Equal(1, batch.NewMissionCount);
        Assert.Contains("LISTO PARA PUBLICAR", batch.FormatPreview(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Q.q[100004]", batch.FormatPreview(), StringComparison.Ordinal);
    }

    [Fact]
    public void Post_bd_client_published_may_still_need_repair_check()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.QuestsEsPublished = true;
        mission.Nombre = "PRUEBA";
        mission.Stages.Add(new MissionStageDraft
        {
            Id = 5501,
            Nombre = "Prueba",
            Descripcion = "Descripción de PRUEBA",
            Rewards = MissionRewardsDraft.FromRaw("200000|200000|30500,1|null|null|null|null"),
        });
        mission.Stages[0].Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4661,
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "7411",
            Detalle = "Descubre el mapa #7411.",
        });
        Assert.False(QuestsEsPublishBatchBuilder.IsPendingQuestsEs(mission));
        Assert.True(QuestsEsPublishBatchBuilder.MayNeedQuestsEsRepair(mission));
        // Repair-only must NOT auto-trigger CONT.8 quests_es on dialog/npc publishes.
        Assert.False(ContentClientRemotePublishService.HasPendingQuestsEs(ws));
    }

    [Fact]
    public void Encode_rewards_xp_kamas_objeto_order_matches_real_swf()
    {
        var slots = QuestsEsCodec.EncodeRewards(
            MissionRewardsDraft.FromRaw("200000|200000|30500,1|null|null|null|null"));
        Assert.Null(slots[0]);
        Assert.Null(slots[1]);
        Assert.Null(slots[2]);
        Assert.Equal(200000, slots[4]);
        Assert.Equal(200000, slots[5]);
        var objetos = Assert.IsType<object[]>(slots[3]);
        var pair = Assert.IsType<int[]>(objetos[0]);
        Assert.Equal(new[] { 1, 30500 }, pair);
    }

    [Fact]
    public void Encode_discover_map_uses_technical_map_id_not_detalle()
    {
        var p = QuestsEsCodec.EncodeObjectiveParameters(new MissionObjectiveDraft
        {
            Id = 4661,
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "7411",
            Detalle = "Descubre el mapa #7411.",
        });
        Assert.Equal(new object?[] { 7411 }, p);
    }

    [Fact]
    public void Quest_name_round_trip_is_string_not_object_coercion()
    {
        var addition = BuildPruebaAssignment();
        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = QuestsEsSeed.Create(20),
            Additions = new[] { addition },
        });
        Assert.True(gen.Success, gen.Error);
        var q = gen.OutputSnapshot!.Quests[100004];
        Assert.Equal("PRUEBA", q.Name);
        Assert.DoesNotContain("object Object", q.Name, StringComparison.OrdinalIgnoreCase);
        Assert.True(q.IsStringName);
        Assert.Empty(q.StageIds);
    }

    [Fact]
    public void Merge_replace_broken_prueba_from_real_1276_when_present()
    {
        var brokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "content-client-publish",
            "quests_es_1276.swf");
        if (!File.Exists(brokenPath))
            return;

        var source = File.ReadAllBytes(brokenPath);
        var before = QuestsEsParser.Parse(source);
        Assert.Equal(1276, before.Version);
        Assert.Equal("PRUEBA", before.Quests[100004].Name);
        Assert.Empty(before.Objectives[4661].Parameters);
        Assert.Equal(
            "[200000,200000,[[30500,1]],null,null,null]",
            QuestsEsCodec.FormatRewards(before.Stages[5501].Rewards));

        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = source,
            Additions = new[] { BuildPruebaAssignment() },
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(1277, gen.TargetVersion);
        var after = gen.OutputSnapshot!;
        Assert.Equal("PRUEBA", after.Quests[100004].Name);
        Assert.Equal(new object?[] { 7411 }, after.Objectives[4661].Parameters.ToArray());
        Assert.Equal(
            "[null,null,null,[[1,30500]],200000,200000]",
            QuestsEsCodec.FormatRewards(after.Stages[5501].Rewards));
        Assert.Equal(before.Quests[519].Name, after.Quests[519].Name);
        Assert.Equal(
            QuestsEsCodec.FormatRewards(before.Stages[704].Rewards),
            QuestsEsCodec.FormatRewards(after.Stages[704].Rewards));
    }

    [Fact]
    public void Batch_replace_broken_remote_prueba_when_published_flag_set()
    {
        var brokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "content-client-publish",
            "quests_es_1276.swf");
        if (!File.Exists(brokenPath))
            return;

        var snap = QuestsEsParser.Parse(File.ReadAllBytes(brokenPath));
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.QuestsEsPublished = true;
        mission.QuestsEsPublishedVersion = 1276;
        mission.Nombre = "PRUEBA";
        mission.Stages.Add(new MissionStageDraft
        {
            Id = 5501,
            Nombre = "Prueba",
            Descripcion = "Descripción de PRUEBA",
            Rewards = MissionRewardsDraft.FromRaw("200000|200000|30500,1|null|null|null|null"),
        });
        mission.Stages[0].Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4661,
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "7411",
            Detalle = "Descubre el mapa #7411.",
        });

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.Equal(1, batch.NewMissionCount);
        Assert.Equal("replace", batch.Bindings[0].Kind);
        Assert.Contains("reparar", batch.FormatPreview(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p: [7411]", batch.FormatPreview(), StringComparison.Ordinal);
        Assert.Contains("[[1,30500]]", batch.FormatPreview(), StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_skips_replace_when_remote_already_matches()
    {
        var assignment = BuildPruebaAssignment();
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(30, new[] { assignment }));
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.QuestsEsPublished = true;
        mission.Nombre = "PRUEBA";
        mission.Stages.Add(new MissionStageDraft
        {
            Id = 5501,
            Nombre = "Prueba",
            Descripcion = "Descripción de PRUEBA",
            Rewards = MissionRewardsDraft.FromRaw("200000|200000|30500,1|null|null|null|null"),
        });
        mission.Stages[0].Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4661,
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "7411",
            Detalle = "Descubre el mapa #7411.",
        });

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.Equal(0, batch.NewMissionCount);
    }

    private static QuestsEsMissionAssignment BuildPruebaAssignment() =>
        new()
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
                    Description = "Descripción de PRUEBA",
                    Rewards = MissionRewardsDraft.FromRaw("200000|200000|30500,1|null|null|null|null"),
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4661,
                    Type = MissionObjectiveTypes.DiscoverMap,
                    Args = "7411",
                    Detalle = "Descubre el mapa #7411.",
                },
            ],
        };
}
