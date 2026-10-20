using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionAutoReturnSyncTests
{
    private static Func<int, (int X, int Y)?> Coords(int x = 4, int y = -19) =>
        _ => (x, y);

    private static bool Sync(ContentDraftWorkspace ws, MissionDraft mission, int x = 4, int y = -19) =>
        ws.Missions.SyncAutoReturns(mission, Coords(x, y));

    private static (ContentDraftWorkspace Ws, MissionDraft Mission, MissionStageDraft Stage) NewDraft(
        int? startNpcId = 20065)
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "AutoReturnTest";
        mission.StartNpcId = startNpcId;
        mission.PublishedBd = false;
        var stage = ws.Missions.AddStage(mission);
        stage.Nombre = "Etapa";
        stage.Rewards = MissionRewardsDraft.FromRaw("100|50|null|null|null|null|null");
        return (ws, mission, stage);
    }

    private static MissionObjectiveDraft AddMap(ContentDraftWorkspace ws, MissionStageDraft stage, int mapId)
    {
        var o = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        o.Args = MissionObjectiveArgsCodec.BuildDiscoverMap(mapId);
        o.Detalle = MissionObjectiveArgsCodec.SuggestDetalle(MissionObjectiveTypes.DiscoverMap, o.Args, "");
        return o;
    }

    [Fact]
    public void Single_map_adds_one_auto_return_at_end_with_xy()
    {
        var (ws, mission, stage) = NewDraft();
        var map = AddMap(ws, stage, 7411);
        Assert.True(Sync(ws, mission, 4, -19));

        Assert.Equal(2, stage.Objectives.Count);
        Assert.Same(map, stage.Objectives[0]);
        var ret = stage.Objectives[1];
        Assert.True(ret.IsAutoReturnToStartNpc);
        Assert.Equal(MissionObjectiveTypes.ReturnToNpc, ret.Tipo);
        Assert.Equal("[20065], x: 4, y: -19", ret.Args);
        Assert.Equal("[20065], x: 4, y: -19", ret.Detalle);
        Assert.Equal("0", ret.EsAlHablar);
        Assert.Equal(0, ret.EsOculto);
        Assert.Equal($"{map.Id}|{ret.Id}", stage.BuildObjetivosField());
        var (x, y) = QuestsEsCodec.ReadObjectiveCoords(ret);
        Assert.Equal(4, x);
        Assert.Equal(-19, y);
        Assert.Equal(new object?[] { 20065 }, QuestsEsCodec.EncodeObjectiveParameters(ret));
    }

    [Fact]
    public void Without_coords_does_not_create_auto_return()
    {
        var (ws, mission, stage) = NewDraft();
        AddMap(ws, stage, 7411);
        Assert.False(ws.Missions.SyncAutoReturns(mission, _ => null));
        Assert.Single(stage.Objectives);
        Assert.DoesNotContain(stage.Objectives, o => o.IsAutoReturnToStartNpc);
        Assert.True(MissionAutoReturnSync.NeedsCoordsForAutoReturn(mission, stage, _ => null));
    }

    [Fact]
    public void Resolve_from_npc_location_map_lookup()
    {
        var npc = NpcsModeloDraft.CreateWithDefaults(20067);
        var loc = new NpcLocationDraft { MapId = 10286, CellId = 311 };
        npc.Locations.Add(loc);

        var resolved = MissionAutoReturnSync.TryResolveNpcWorldCoords(
            20067,
            new[] { npc },
            mapId => mapId == 10286 ? (1, 1) : null);
        Assert.Equal((1, 1), resolved);

        var (ws, mission, stage) = NewDraft(20067);
        AddMap(ws, stage, 10306);
        Assert.True(Sync(ws, mission, 1, 1));
        var ret = Assert.Single(stage.Objectives, o => o.IsAutoReturnToStartNpc);
        Assert.Equal("[20067], x: 1, y: 1", ret.Args);
    }

    [Fact]
    public void Several_maps_share_one_auto_return()
    {
        var (ws, mission, stage) = NewDraft();
        var a = AddMap(ws, stage, 7411);
        var b = AddMap(ws, stage, 7420);
        var c = AddMap(ws, stage, 7430);
        Sync(ws, mission);

        Assert.Equal(4, stage.Objectives.Count);
        Assert.Single(stage.Objectives.Where(o => o.IsAutoReturnToStartNpc));
        Assert.True(stage.Objectives[^1].IsAutoReturnToStartNpc);
        Assert.Equal($"{a.Id}|{b.Id}|{c.Id}|{stage.Objectives[^1].Id}", stage.BuildObjetivosField());
    }

    [Fact]
    public void StartNpcId_change_updates_auto_return_args()
    {
        var (ws, mission, stage) = NewDraft(20065);
        AddMap(ws, stage, 7411);
        Sync(ws, mission);
        mission.StartNpcId = 20070;
        Assert.True(Sync(ws, mission, 5, -6));
        Assert.Equal("[20070], x: 5, y: -6", stage.Objectives[^1].Args);
        Assert.Equal("[20070], x: 5, y: -6", stage.Objectives[^1].Detalle);
    }

    [Fact]
    public void Removing_all_maps_removes_auto_return()
    {
        var (ws, mission, stage) = NewDraft();
        var map = AddMap(ws, stage, 7411);
        Sync(ws, mission);
        Assert.True(ws.Missions.RemoveObjective(stage, map));
        Assert.True(Sync(ws, mission));
        Assert.Empty(stage.Objectives);
    }

    [Fact]
    public void Re_adding_map_recreates_auto_return()
    {
        var (ws, mission, stage) = NewDraft();
        var map = AddMap(ws, stage, 7411);
        Sync(ws, mission);
        ws.Missions.RemoveObjective(stage, map);
        Sync(ws, mission);
        AddMap(ws, stage, 7420);
        Sync(ws, mission);
        Assert.Equal(2, stage.Objectives.Count);
        Assert.True(stage.Objectives[^1].IsAutoReturnToStartNpc);
    }

    [Fact]
    public void Does_not_duplicate_auto_return()
    {
        var (ws, mission, stage) = NewDraft();
        AddMap(ws, stage, 7411);
        Sync(ws, mission);
        Assert.False(Sync(ws, mission));
        Assert.Single(stage.Objectives.Where(o => o.IsAutoReturnToStartNpc));
    }

    [Fact]
    public void Preserves_manual_tipo9()
    {
        var (ws, mission, stage) = NewDraft();
        AddMap(ws, stage, 7411);
        var manual = ws.Missions.AddObjective(stage, MissionObjectiveTypes.ReturnToNpc);
        manual.Args = "[99999]";
        manual.Detalle = "[99999]";
        manual.IsAutoReturnToStartNpc = false;
        Sync(ws, mission);

        Assert.Contains(stage.Objectives, o => !o.IsAutoReturnToStartNpc && o.Tipo == 9 && o.Args == "[99999]");
        Assert.Single(stage.Objectives.Where(o => o.IsAutoReturnToStartNpc));
        Assert.True(stage.Objectives[^1].IsAutoReturnToStartNpc);
        Assert.Equal(3, stage.Objectives.Count);
    }

    [Fact]
    public void Without_StartNpcId_does_not_create_invalid_objective()
    {
        var (ws, mission, stage) = NewDraft(null);
        AddMap(ws, stage, 7411);
        Assert.False(Sync(ws, mission));
        Assert.Single(stage.Objectives);
        Assert.True(MissionAutoReturnSync.NeedsStartNpcForAutoReturn(mission, stage));
    }

    [Fact]
    public void Objective_id_uses_batch_allocator()
    {
        var (ws, mission, stage) = NewDraft();
        ws.Missions.SetDbMaxObjectiveId(5000);
        AddMap(ws, stage, 7411);
        Sync(ws, mission);
        Assert.True(stage.Objectives[^1].Id > 5000);
    }

    [Fact]
    public void Quests_es_encodes_tipo9_npc_param_and_xy()
    {
        var (ws, mission, stage) = NewDraft(20065);
        AddMap(ws, stage, 7411);
        Sync(ws, mission, 24, -6);
        var ret = stage.Objectives[^1];
        var p = QuestsEsCodec.EncodeObjectiveParameters(ret);
        Assert.Equal(new object?[] { 20065 }, p);
        Assert.Equal((24, -6), QuestsEsCodec.ReadObjectiveCoords(ret));
    }

    [Fact]
    public void Stage_rewards_remain_on_stage_not_on_last_map_only()
    {
        var (ws, mission, stage) = NewDraft();
        AddMap(ws, stage, 7411);
        AddMap(ws, stage, 7420);
        Sync(ws, mission);
        Assert.Equal(100, stage.Rewards.Exp);
        Assert.Equal(50, stage.Rewards.Kamas);
        Assert.True(stage.Objectives[^1].IsAutoReturnToStartNpc);
        Assert.DoesNotContain(stage.Objectives.Take(stage.Objectives.Count - 1), o => o.IsAutoReturnToStartNpc);
    }

    [Fact]
    public void Published_mission_100004_style_not_mutated()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.StartNpcId = 20065;
        mission.Nombre = "PRUEBA";
        var stage = ws.Missions.AddStage(mission);
        stage.Id = 5501;
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Id = 4661;
        map.Args = "7411";
        mission.MarkPublishedSnapshot();
        Assert.False(ws.Missions.SyncAutoReturns(mission));
        Assert.Single(stage.Objectives);
        Assert.False(stage.Objectives[0].IsAutoReturnToStartNpc);
    }

    [Fact]
    public void Published_with_pending_bd_changes_still_no_auto_return()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.StartNpcId = 20065;
        mission.Nombre = "PRUEBA";
        var stage = ws.Missions.AddStage(mission);
        stage.Id = 5501;
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Id = 4661;
        map.Args = "7411";
        mission.MarkPublishedSnapshot();
        mission.MarkPendingBdEdit();
        Assert.True(mission.HasPendingBdChanges);
        Assert.False(ws.Missions.SyncAutoReturns(mission));
        Assert.Single(stage.Objectives);
        Assert.Equal(4661, stage.Objectives[0].Id);
        Assert.DoesNotContain(stage.Objectives, o => o.IsAutoReturnToStartNpc);
    }

    [Fact]
    public void Published_strips_local_only_auto_return_not_in_snapshot()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.StartNpcId = 20065;
        var stage = ws.Missions.AddStage(mission);
        stage.Id = 5501;
        var map = ws.Missions.AddObjective(stage, MissionObjectiveTypes.DiscoverMap);
        map.Id = 4661;
        map.Args = "7411";
        mission.MarkPublishedSnapshot();
        stage.Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4662,
            Tipo = MissionObjectiveTypes.ReturnToNpc,
            Args = "[20065]",
            Detalle = "[20065]",
            IsAutoReturnToStartNpc = true,
        });
        Assert.True(ws.Missions.SyncAutoReturns(mission));
        Assert.Single(stage.Objectives);
        Assert.Equal(4661, stage.Objectives[0].Id);
    }

    [Fact]
    public void Unpublished_draft_with_map_still_gets_auto_return()
    {
        var (ws, mission, stage) = NewDraft();
        Assert.False(MissionAutoReturnSync.IsPublishedLocked(mission));
        AddMap(ws, stage, 7411);
        Assert.True(Sync(ws, mission));
        Assert.Contains(stage.Objectives, o => o.IsAutoReturnToStartNpc);
    }

    [Fact]
    public void Fix9_quests_es_repair_preview_excludes_auto_return_4662()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "content-client-publish",
            "quests_es_1277.swf");
        if (!File.Exists(path))
            return;

        var snap = QuestsEsParser.Parse(File.ReadAllBytes(path));
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        // Distinct name forces replace binding even if remote already says PRUEBA.
        mission.Nombre = "PRUEBA";
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.QuestsEsPublished = true;
        mission.QuestsEsPublishedVersion = 1277;
        mission.StartNpcId = 20065;
        mission.HasPendingBdChanges = true;
        var stage = new MissionStageDraft
        {
            Id = 5501,
            Nombre = "Prueba RENOVADA",
            Descripcion = "x",
            Rewards = MissionRewardsDraft.FromRaw("200000|200000|30500,1|null|null|null|null"),
        };
        stage.Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4661,
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "7411",
            Detalle = "",
        });
        stage.Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4662,
            Tipo = MissionObjectiveTypes.ReturnToNpc,
            Args = "[20065]",
            IsAutoReturnToStartNpc = true,
        });
        mission.Stages.Add(stage);
        mission.PublishedStageIds = new List<int> { 5501 };
        mission.PublishedObjectiveIds = new List<int> { 4661 };

        Assert.True(ws.Missions.SyncAutoReturns(mission));
        Assert.DoesNotContain(mission.Stages[0].Objectives, o => o.Id == 4662);

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        var preview = batch.FormatPreview();
        Assert.DoesNotContain("Q.o[4662]", preview, StringComparison.Ordinal);
        Assert.Contains("Q.q[100004]", preview, StringComparison.Ordinal);
        var replace = Assert.Single(batch.Bindings, b => b.Assignment is not null);
        Assert.DoesNotContain(replace.Assignment!.Objectives, o => o.ObjectiveId == 4662);
        Assert.Equal("PRUEBA", replace.Assignment.Quest.Name);
        Assert.Equal(new[] { 5501 }, replace.Assignment.Quest.StageIds.ToArray());
    }

    [Fact]
    public void Cannot_remove_auto_return_via_batch()
    {
        var (ws, mission, stage) = NewDraft();
        AddMap(ws, stage, 7411);
        Sync(ws, mission);
        var ret = stage.Objectives[^1];
        Assert.False(ws.Missions.RemoveObjective(stage, ret));
        Assert.Contains(ret, stage.Objectives);
    }

    [Fact]
    public void Format_label_pending_without_npc()
    {
        Assert.Contains("PENDIENTE", MissionAutoReturnSync.FormatAutoReturnLabel(null, null), StringComparison.Ordinal);
        Assert.Contains("20065", MissionAutoReturnSync.FormatAutoReturnLabel(20065, null), StringComparison.Ordinal);
        Assert.Equal("Vuelve con RUFUS", MissionAutoReturnSync.FormatAutoReturnLabel(20065, _ => "RUFUS"));
    }
}
