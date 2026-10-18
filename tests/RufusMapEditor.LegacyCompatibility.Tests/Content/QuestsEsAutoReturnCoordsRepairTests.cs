using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class QuestsEsAutoReturnCoordsRepairTests
{
    private static QuestsEsSnapshot SnapWith4663Incomplete()
    {
        var quests = new Dictionary<int, QuestsEsQuestEntry>
        {
            [100005] = new QuestsEsQuestEntry
            {
                Name = "Guerreros del poder",
                IsStringName = true,
                StageIds = Array.Empty<int>(),
            },
        };
        var stages = new Dictionary<int, QuestsEsStageEntry>
        {
            [5502] = new QuestsEsStageEntry
            {
                Name = "Misiones faciles prueba",
                Description = "Esto es una prueba.",
                Rewards = QuestsEsCodec.EncodeRewards(
                    MissionRewardsDraft.FromRaw("200000|20000|6934,2|null|null|null|null")),
                LevelBands = Array.Empty<object?>(),
            },
        };
        var objectives = new Dictionary<int, QuestsEsObjectiveEntry>
        {
            [4662] = new QuestsEsObjectiveEntry
            {
                Type = 4,
                Parameters = new object?[] { 10306 },
            },
            [4663] = new QuestsEsObjectiveEntry
            {
                Type = 9,
                Parameters = new object?[] { 20067 },
                X = null,
                Y = null,
            },
        };
        return new QuestsEsSnapshot
        {
            Version = 1280,
            SwfVersion = 1280,
            WasCompressed = false,
            Signature = "FWS",
            Quests = quests,
            Stages = stages,
            Objectives = objectives,
            TypeTemplates = new Dictionary<int, string> { [9] = "Vuelve a ver a #1" },
            QuestAssignmentCount = quests.Count,
            StageAssignmentCount = stages.Count,
            ObjectiveAssignmentCount = objectives.Count,
            HasFileEnd = true,
            ConstantPoolCount = 0,
            DoActionCount = 1,
        };
    }

    private static QuestsEsSnapshot EmptySnap(int version = 1280) => new()
    {
        Version = version,
        SwfVersion = version,
        WasCompressed = false,
        Signature = "FWS",
        Quests = new Dictionary<int, QuestsEsQuestEntry>(),
        Stages = new Dictionary<int, QuestsEsStageEntry>(),
        Objectives = new Dictionary<int, QuestsEsObjectiveEntry>(),
        TypeTemplates = new Dictionary<int, string>(),
        QuestAssignmentCount = 0,
        StageAssignmentCount = 0,
        ObjectiveAssignmentCount = 0,
        HasFileEnd = true,
        ConstantPoolCount = 0,
        DoActionCount = 1,
    };

    private static ContentDraftWorkspace Workspace100005(int startNpcId = 20066, int mapId = 10286)
    {
        var ws = new ContentDraftWorkspace();
        var npc = NpcsModeloDraft.CreateWithDefaults(startNpcId);
        npc.Nombre = "Ruben Martin";
        npc.PublishedBd = true;
        npc.Locations.Add(new NpcLocationDraft { MapId = mapId, CellId = 311 });
        ws.Npcs.ImportPreservingId(npc);

        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Guerreros del poder";
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100005;
        mission.QuestsEsPublished = true;
        mission.QuestsEsPublishedVersion = 1280;
        mission.StartNpcId = startNpcId;
        mission.Stages.Clear();

        var stage = new MissionStageDraft
        {
            Id = 5502,
            Nombre = "Misiones faciles prueba",
            Descripcion = "Esto es una prueba.",
            Rewards = MissionRewardsDraft.FromRaw("200000|20000|6934,2|null|null|null|null"),
        };
        stage.Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4662,
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "10306",
            Detalle = "Descubre el mapa #10306.",
        });
        stage.Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4663,
            Tipo = MissionObjectiveTypes.ReturnToNpc,
            Args = "[20067]",
            Detalle = "[20067]",
            IsAutoReturnToStartNpc = true,
        });
        mission.Stages.Add(stage);
        mission.PublishedStageIds = new List<int> { 5502 };
        mission.PublishedObjectiveIds = new List<int> { 4662, 4663 };
        mission.MarkPublishedSnapshot();
        return ws;
    }

    [Fact]
    public void Map_valid_reads_xy_via_lookup()
    {
        var resolver = new MapWorldCoordinatesResolver(id => id == 10286 ? (1, 1) : null);
        Assert.Equal((1, 1), resolver.TryResolve(10286));
        Assert.Null(resolver.TryResolve(999999));
    }

    [Fact]
    public void Cell_id_ignored_only_map_id_used()
    {
        var npc = NpcsModeloDraft.CreateWithDefaults(20066);
        npc.Locations.Add(new NpcLocationDraft { MapId = 10286, CellId = 99999 });
        var coords = MissionAutoReturnSync.TryResolveNpcWorldCoords(
            20066, new[] { npc }, mapId => mapId == 10286 ? (1, 1) : null);
        Assert.Equal((1, 1), coords);
    }

    [Fact]
    public void Map_change_resyncs_xy_on_unpublished()
    {
        var ws = new ContentDraftWorkspace();
        var npc = NpcsModeloDraft.CreateWithDefaults(20065);
        npc.Locations.Add(new NpcLocationDraft { MapId = 100, CellId = 1 });
        ws.Npcs.ImportPreservingId(npc);
        Assert.Contains(ws.Npcs.Drafts, d => d.Id == 20065 && d.Locations.Any(l => l.MapId == 100));

        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = false;
        mission.StartNpcId = 20065;
        mission.Stages.Clear();
        var stage = ws.Missions.AddStage(mission);
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Args = "7411";

        (int X, int Y)? Resolve(int nid) =>
            MissionAutoReturnSync.TryResolveNpcWorldCoords(
                nid,
                ws.Npcs.Drafts,
                id => id switch
                {
                    100 => (4, -19),
                    200 => (9, 0),
                    _ => null,
                });

        Assert.Equal((4, -19), Resolve(20065));
        Assert.True(ws.Missions.SyncAutoReturns(mission, Resolve));
        Assert.Equal("[20065], x: 4, y: -19", stage.Objectives[^1].Args);

        var live = ws.Npcs.Drafts.First(d => d.Id == 20065);
        live.Locations[0].MapId = 200;
        Assert.True(ws.Missions.SyncAutoReturns(mission, Resolve));
        Assert.Equal("[20065], x: 9, y: 0", stage.Objectives[^1].Args);
    }

    [Fact]
    public void Missing_map_blocks_auto_return()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.StartNpcId = 20065;
        var stage = ws.Missions.AddStage(mission);
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Args = "7411";
        Assert.False(ws.Missions.SyncAutoReturns(mission, _ => null));
        Assert.DoesNotContain(stage.Objectives, o => o.IsAutoReturnToStartNpc);
    }

    [Fact]
    public void New_t9_contains_npc_xy()
    {
        var ws = new ContentDraftWorkspace();
        var npc = NpcsModeloDraft.CreateWithDefaults(20065);
        npc.Locations.Add(new NpcLocationDraft { MapId = 10286, CellId = 0 });
        ws.Npcs.ImportPreservingId(npc);
        var mission = ws.Missions.CreateMission();
        mission.StartNpcId = 20065;
        var stage = ws.Missions.AddStage(mission);
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Args = "10306";
        Assert.True(ws.Missions.SyncAutoReturns(mission, nid =>
            MissionAutoReturnSync.TryResolveNpcWorldCoords(nid, ws.Npcs.Drafts, id => (1, 1))));
        var ret = Assert.Single(stage.Objectives, o => o.IsAutoReturnToStartNpc);
        Assert.Equal(9, ret.Tipo);
        Assert.Equal("[20065], x: 1, y: 1", ret.Args);
        Assert.Equal(new object?[] { 20065 }, QuestsEsCodec.EncodeObjectiveParameters(ret));
        var (x, y) = QuestsEsCodec.ReadObjectiveCoords(ret);
        Assert.Equal(1, x);
        Assert.Equal(1, y);
    }

    [Fact]
    public void Repair_4663_keeps_id_and_only_qo()
    {
        var ws = Workspace100005();
        var snap = SnapWith4663Incomplete();
        var batch = QuestsEsPublishBatchBuilder.BuildWithRepairs(
            ws, snap, mapId => mapId == 10286 ? (1, 1) : null);

        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.NotNull(batch.AutoReturnCoordsRepair);
        Assert.Equal(4663, batch.AutoReturnCoordsRepair!.ObjectiveId);
        Assert.Equal(20067, batch.AutoReturnCoordsRepair.NpcIdCurrent);
        Assert.Equal(20066, batch.AutoReturnCoordsRepair.NpcIdCorrect);
        Assert.Equal(10286, batch.AutoReturnCoordsRepair.NpcMapId);
        Assert.Equal(1, batch.AutoReturnCoordsRepair.X);
        Assert.Equal(1, batch.AutoReturnCoordsRepair.Y);
        Assert.True(batch.AutoReturnCoordsRepair.BdNeedsUpdate);

        var assign = Assert.Single(batch.Bindings).Assignment!;
        Assert.False(assign.WriteQuest);
        Assert.Empty(assign.Stages);
        var obj = Assert.Single(assign.Objectives);
        Assert.Equal(4663, obj.ObjectiveId);
        Assert.Equal(9, obj.Type);
        Assert.Equal("[20066], x: 1, y: 1", obj.Args);

        var preview = batch.FormatPreview();
        Assert.Contains("AUTO-RETURN REPAIR", preview, StringComparison.Ordinal);
        Assert.Contains("20066", preview, StringComparison.Ordinal);
        Assert.Contains("UPDATE args", preview, StringComparison.Ordinal);
        Assert.Contains("Q.o[4663]", preview, StringComparison.Ordinal);
        Assert.Contains("x: 1", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Repair_without_coords_source_errors()
    {
        var ws = Workspace100005(startNpcId: 20066, mapId: 0);
        ws.Npcs.Drafts[0].Locations.Clear();
        var snap = SnapWith4663Incomplete();
        var batch = QuestsEsPublishBatchBuilder.BuildWithRepairs(
            ws, snap, _ => null);
        Assert.False(batch.IsValid);
        Assert.Contains(QuestsEsAutoReturnCoordsRepair.CoordsUnavailableMessage, batch.Errors);
    }

    [Fact]
    public void Published_lock_still_blocks_other_autos()
    {
        var ws = Workspace100005();
        var stage = ws.Missions.Missions[0].Stages[0];
        stage.Objectives.Add(new MissionObjectiveDraft
        {
            Id = 99999,
            Tipo = MissionObjectiveTypes.ReturnToNpc,
            Args = "[20066]",
            IsAutoReturnToStartNpc = true,
        });
        Assert.True(MissionAutoReturnSync.IsPublishedLocked(ws.Missions.Missions[0]));
        Assert.True(ws.Missions.SyncAutoReturns(ws.Missions.Missions[0], _ => (1, 1)));
        Assert.DoesNotContain(stage.Objectives, o => o.Id == 99999);
        Assert.Contains(stage.Objectives, o => o.Id == 4663);
    }

    [Fact]
    public void Preflight_blocks_incomplete_auto_return_new_mission()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Nueva";
        mission.PublishedBd = false;
        mission.StartNpcId = 20065;
        mission.Stages.Clear();
        var stage = ws.Missions.AddStage(mission);
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Args = "10306";
        Assert.False(ws.Missions.SyncAutoReturns(mission, _ => null));
        Assert.DoesNotContain(stage.Objectives, o => o.IsAutoReturnToStartNpc);
        Assert.True(MissionAutoReturnSync.NeedsCoordsForAutoReturn(mission, stage, _ => null));
        Assert.Equal(
            QuestsEsAutoReturnCoordsRepair.CoordsUnavailableMessage,
            MissionAutoReturnSync.PendingCoordsMessage);
    }

    [Fact]
    public void Bd_map_id_preferred_over_draft()
    {
        var npc = NpcsModeloDraft.CreateWithDefaults(20066);
        npc.Locations.Add(new NpcLocationDraft { MapId = 111, CellId = 1 });
        var mapId = QuestsEsAutoReturnCoordsRepair.TryResolveMapIdForNpc(
            20066, new[] { npc }, _ => 10286);
        Assert.Equal(10286, mapId);
    }

    [Fact]
    public void Cross_npc_fallback_forbidden()
    {
        var npcA = NpcsModeloDraft.CreateWithDefaults(20067);
        var npcB = NpcsModeloDraft.CreateWithDefaults(20066);
        npcB.Locations.Add(new NpcLocationDraft { MapId = 10286, CellId = 311 });
        // 20067 has no location — must NOT use 20066's map.
        Assert.Null(QuestsEsAutoReturnCoordsRepair.TryResolveMapIdForNpc(
            20067, new[] { npcA, npcB }, _ => null));
        Assert.Equal(10286, QuestsEsAutoReturnCoordsRepair.TryResolveMapIdForNpc(
            20066, new[] { npcA, npcB }, _ => null));
    }

    [Fact]
    public void Remap_leading_npc_on_publish_state()
    {
        var ws = new ContentDraftWorkspace();
        var npc = NpcsModeloDraft.CreateWithDefaults(20067);
        npc.Locations.Add(new NpcLocationDraft { MapId = 10286, CellId = 311 });
        ws.Npcs.ImportPreservingId(npc);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "T";
        mission.StartNpcId = 20067;
        mission.Stages.Clear();
        var stage = ws.Missions.AddStage(mission);
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Args = "10306";
        Assert.True(ws.Missions.SyncAutoReturns(mission, _ => (1, 1)));
        var ret = Assert.Single(stage.Objectives, o => o.IsAutoReturnToStartNpc);
        Assert.Equal("[20067], x: 1, y: 1", ret.Args);

        var plan = new ContentPublishPlan
        {
            Maxes = new ContentPublishMaxSnapshot(),
        };
        plan.NpcIdMap[20067] = 20066;
        plan.QuestIdMap[mission.DraftId] = 100005;
        plan.StageIdMap[stage.Id] = 5502;
        plan.ObjectiveIdMap[map.Id] = 4662;
        plan.ObjectiveIdMap[ret.Id] = 4663;

        ContentPublishService.ApplyPublishedState(ws, plan);
        Assert.Equal(20066, mission.StartNpcId);
        Assert.Equal(20066, ws.Npcs.Drafts.First(d => d.PublishedBd).Id);
        var auto = Assert.Single(mission.Stages[0].Objectives, o => o.IsAutoReturnToStartNpc);
        Assert.Equal(4663, auto.Id);
        Assert.StartsWith("[20066]", auto.Args, StringComparison.Ordinal);
    }

    [Fact]
    public void Remap_leading_npc_helper()
    {
        var map = new Dictionary<int, int> { [20067] = 20066 };
        Assert.Equal("[20066], x: 1, y: 1",
            MissionObjectiveNpcRemap.RemapLeadingNpc("[20067], x: 1, y: 1", map));
        Assert.Equal("[20066,10,1]",
            MissionObjectiveNpcRemap.RemapLeadingNpc("[20067,10,1]", map));
    }

    [Fact]
    public void Npc_name_label_resolvable_when_known()
    {
        Assert.Equal(
            "Vuelve con Ruben Martin",
            MissionAutoReturnSync.FormatAutoReturnLabel(20066, _ => "Ruben Martin"));
    }
}
