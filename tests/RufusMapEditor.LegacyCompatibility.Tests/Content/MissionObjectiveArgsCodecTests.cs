using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionObjectiveArgsCodecTests
{
    [Fact]
    public void Talk_type1_builds_npc_bracket()
    {
        Assert.Equal("[20003]", MissionObjectiveArgsCodec.BuildTalk(20003, null, null));
        Assert.Equal("[20003], x: 1, y: -2", MissionObjectiveArgsCodec.BuildTalk(20003, 1, -2));
    }

    [Fact]
    public void Show_and_deliver_share_npc_item_qty()
    {
        Assert.Equal("[20003,8682,5]", MissionObjectiveArgsCodec.BuildShowOrDeliver(20003, 8682, 5, null, null));
    }

    [Fact]
    public void Discover_map_is_bare_mapId()
    {
        Assert.Equal("7411", MissionObjectiveArgsCodec.BuildDiscoverMap(7411));
    }

    [Fact]
    public void Discover_area_is_bare_areaId()
    {
        Assert.Equal("12", MissionObjectiveArgsCodec.BuildDiscoverArea(12));
    }

    [Fact]
    public void Defeat_mobs_pairs()
    {
        Assert.Equal("[36,5]", MissionObjectiveArgsCodec.BuildDefeatMobs([(36, 5)], null, null));
        Assert.Equal("[36,5,37,2]", MissionObjectiveArgsCodec.BuildDefeatMobs([(36, 5), (37, 2)], null, null));
    }

    [Fact]
    public void Use_item_return_level_spells_jobs()
    {
        Assert.Equal("[8682]", MissionObjectiveArgsCodec.BuildUseItem(8682));
        Assert.Equal("[20003]", MissionObjectiveArgsCodec.BuildTalk(20003, null, null)); // tipo 9 same shape
        Assert.Equal("[50]", MissionObjectiveArgsCodec.BuildReachLevel(50));
        Assert.Equal("[3]", MissionObjectiveArgsCodec.BuildHaveSpells(3));
        Assert.Equal("[2,100]", MissionObjectiveArgsCodec.BuildJobLevel(2, 100));
    }

    [Fact]
    public void Strip_coords_roundtrip()
    {
        var (core, x, y) = MissionObjectiveArgsCodec.StripCoords("[20003], x: 4, y: 5");
        Assert.Equal("[20003]", core);
        Assert.Equal(4, x);
        Assert.Equal(5, y);
    }

    [Fact]
    public void Suggest_detalle_human_for_talk()
    {
        var d = MissionObjectiveArgsCodec.SuggestDetalle(MissionObjectiveTypes.TalkToNpc, "[20003]");
        Assert.Contains("20003", d);
        Assert.Contains("Habla", d);
    }
}

public sealed class MissionContent20DraftTests
{
    [Fact]
    public void Empty_mission_optional_until_created()
    {
        var ws = new ContentDraftWorkspace();
        Assert.Empty(ws.Missions.Missions);
        var m = ws.Missions.CreateMission();
        m.StartNpcId = 99;
        Assert.Single(ws.Missions.Missions);
        Assert.Equal(99, m.StartNpcId);
    }

    [Fact]
    public void Multiple_stages_order_builds_etapas_csv()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(100);
        batch.SetDbMaxObjectiveId(10);
        var m = batch.CreateMission();
        var a = batch.AddStage(m);
        a.Nombre = "Uno";
        var b = batch.AddStage(m);
        b.Nombre = "Dos";
        var c = batch.AddStage(m);
        c.Nombre = "Tres";
        Assert.Equal("101,102,103", m.BuildEtapasCsv());
        Assert.True(batch.MoveStage(m, c, -2));
        Assert.Equal("103,101,102", m.BuildEtapasCsv());
    }

    [Fact]
    public void Add_remove_objective_and_typed_args()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(1);
        batch.SetDbMaxObjectiveId(1);
        var m = batch.CreateMission();
        var s = batch.AddStage(m);
        var o1 = batch.AddObjective(s, MissionObjectiveTypes.TalkToNpc);
        o1.Args = MissionObjectiveArgsCodec.BuildTalk(7, null, null);
        var o2 = batch.AddObjective(s, MissionObjectiveTypes.ShowItemToNpc);
        o2.Args = MissionObjectiveArgsCodec.BuildShowOrDeliver(7, 10, 1, null, null);
        Assert.Equal(2, s.Objectives.Count);
        Assert.True(batch.RemoveObjective(s, o1));
        Assert.Single(s.Objectives);
        Assert.Equal("[7,10,1]", s.Objectives[0].Args);
    }

    [Fact]
    public void All_normal_types_args_shapes()
    {
        Assert.Equal("", MissionObjectiveArgsCodec.BuildManual("texto"));
        Assert.Equal("[1]", MissionObjectiveArgsCodec.BuildTalk(1, null, null));
        Assert.Equal("[1,2,3]", MissionObjectiveArgsCodec.BuildShowOrDeliver(1, 2, 3, null, null));
        Assert.Equal("99", MissionObjectiveArgsCodec.BuildDiscoverMap(99));
        Assert.Equal("5", MissionObjectiveArgsCodec.BuildDiscoverArea(5));
        Assert.Equal("[8,2]", MissionObjectiveArgsCodec.BuildDefeatMobs([(8, 2)], null, null));
        Assert.Equal("[44]", MissionObjectiveArgsCodec.BuildUseItem(44));
        Assert.Equal("[1]", MissionObjectiveArgsCodec.BuildTalk(1, null, null)); // return npc
        Assert.Equal("[20]", MissionObjectiveArgsCodec.BuildReachLevel(20));
        Assert.Equal("[4]", MissionObjectiveArgsCodec.BuildHaveSpells(4));
        Assert.Equal("[1,50]", MissionObjectiveArgsCodec.BuildJobLevel(1, 50));
    }

    [Fact]
    public void Rewards_xp_kamas_items_null_slots()
    {
        var empty = new MissionRewardsDraft();
        Assert.Equal("0|0|null|null|null|null|null", empty.ToRaw());

        var r = new MissionRewardsDraft { Exp = 100, Kamas = 50 };
        r.Objetos.Add(new MissionRewardItem { ItemId = 1, Cantidad = 2 });
        Assert.Equal("100|50|1,2|null|null|null|null", r.ToRaw());
    }

    [Fact]
    public void Draft_persists_across_workspace_roundtrip()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(10);
        ws.Missions.SetDbMaxObjectiveId(20);
        var m = ws.Missions.CreateMission();
        m.Nombre = "Persistida";
        m.StartNpcId = 55;
        var s = ws.Missions.AddStage(m);
        s.Rewards.Exp = 10;
        var o = ws.Missions.AddObjective(s, MissionObjectiveTypes.DiscoverMap);
        o.Args = MissionObjectiveArgsCodec.BuildDiscoverMap(7411);

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        Assert.Single(loaded.Missions.Missions);
        var m2 = loaded.Missions.Missions[0];
        Assert.Equal("Persistida", m2.Nombre);
        Assert.Equal(55, m2.StartNpcId);
        Assert.Equal("7411", m2.Stages[0].Objectives[0].Args);
        Assert.Equal(10, m2.Stages[0].Rewards.Exp);
    }

    [Fact]
    public void Remove_mission_is_local_only_no_db_surface()
    {
        var ws = new ContentDraftWorkspace();
        var m = ws.Missions.CreateMission();
        m.StartNpcId = 1;
        Assert.Equal(MissionDeleteResult.Deleted,
            ws.Missions.TryDeleteMission(m.DraftId, true, null, out _));
        Assert.Empty(ws.Missions.Missions);
        // No quests_es / DB writer invoked — draft API only.
        Assert.Null(typeof(MissionDraftBatch).GetMethod("PublishQuestsEs"));
    }
}
