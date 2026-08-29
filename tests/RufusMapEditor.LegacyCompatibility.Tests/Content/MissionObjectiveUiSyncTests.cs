using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionObjectiveUiSyncTests
{
    private static MissionObjectiveDraft Draft(int tipo = 0) => new() { Id = 1, Tipo = tipo };

    [Theory]
    [InlineData(1, "[7]")]
    public void Auto_update_tipo1_talk(int tipo, string expected)
    {
        var d = Draft(tipo);
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.TalkToNpc,
            NpcId = "7",
        });
        Assert.Null(err);
        Assert.Equal(expected, d.Args);
    }

    [Fact]
    public void Auto_update_tipo2_show()
    {
        var d = Draft();
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.ShowItemToNpc,
            NpcId = "20003",
            ItemId = "8682",
            Qty = "5",
        }));
        Assert.Equal("[20003,8682,5]", d.Args);
    }

    [Fact]
    public void Auto_update_tipo3_deliver()
    {
        var d = Draft();
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DeliverItemsToNpc,
            NpcId = "1",
            ItemId = "2",
            Qty = "3",
        }));
        Assert.Equal("[1,2,3]", d.Args);
    }

    [Fact]
    public void Auto_update_tipo4_map()
    {
        var d = Draft();
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DiscoverMap,
            MapId = "7411",
        }));
        Assert.Equal("7411", d.Args);
    }

    [Fact]
    public void Auto_update_tipo5_area()
    {
        var d = Draft();
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DiscoverArea,
            AreaId = "12",
        }));
        Assert.Equal("12", d.Args);
    }

    [Fact]
    public void Auto_update_tipo6_mobs()
    {
        var d = Draft();
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DefeatMobs,
            MobId = "36",
            Qty = "5",
        }));
        Assert.Equal("[36,5]", d.Args);
    }

    [Fact]
    public void Auto_update_tipo8_use_item()
    {
        var d = Draft();
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.UseItem,
            ItemId = "44",
        }));
        Assert.Equal("[44]", d.Args);
    }

    [Fact]
    public void Auto_update_tipo9_return_npc()
    {
        var d = Draft();
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.ReturnToNpc,
            NpcId = "9",
        }));
        Assert.Equal("[9]", d.Args);
    }

    [Fact]
    public void Auto_update_tipo14_15_16()
    {
        var d = Draft();
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.ReachLevel,
            Level = "50",
        }));
        Assert.Equal("[50]", d.Args);

        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.HaveSpells,
            SpellCount = "3",
        }));
        Assert.Equal("[3]", d.Args);

        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.JobLevel,
            JobCount = "2",
            JobLevel = "100",
        }));
        Assert.Equal("[2,100]", d.Args);
    }

    [Fact]
    public void Human_validation_messages()
    {
        Assert.Equal("La etapa necesita un nombre.", MissionObjectiveUiSync.ValidateStageName(""));
        var d = Draft();
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DeliverItemsToNpc,
            NpcId = "",
            ItemId = "1",
            Qty = "1",
        });
        Assert.Equal("Selecciona el NPC de destino.", err);

        err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DeliverItemsToNpc,
            NpcId = "1",
            ItemId = "",
            Qty = "1",
        });
        Assert.Equal("Selecciona un objeto.", err);

        err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DeliverItemsToNpc,
            NpcId = "1",
            ItemId = "2",
            Qty = "0",
        });
        Assert.Equal("La cantidad debe ser mayor que 0.", err);
    }

    [Fact]
    public void Tipo12_not_selectable_and_blocked()
    {
        Assert.False(MissionObjectiveUiSync.IsType12Selectable);
        Assert.DoesNotContain(MissionObjectiveTypes.DeliverSouls, MissionObjectiveTypes.UiNormalTypes);
        var d = Draft();
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DeliverSouls,
        });
        Assert.Contains("pendiente", err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Raw_rewards_remain_technical_only_format_unchanged()
    {
        var r = new MissionRewardsDraft { Exp = 200000, Kamas = 40000 };
        Assert.Equal("200000|40000|null|null|null|null|null", r.ToRaw());
    }

    [Fact]
    public void Draft_persists_and_no_quests_es_writer()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        m.Nombre = "UX";
        var s = ws.Missions.AddStage(m);
        var o = ws.Missions.AddObjective(s, MissionObjectiveTypes.TalkToNpc);
        Assert.Null(MissionObjectiveUiSync.TryApply(o, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.TalkToNpc,
            NpcId = "5",
        }));
        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        Assert.Equal("[5]", loaded.Missions.Missions[0].Stages[0].Objectives[0].Args);
        Assert.Null(typeof(MissionDraftBatch).GetMethod("PublishQuestsEs"));
        Assert.Null(typeof(MissionDraftBatch).GetMethod("WriteDatabase"));
    }
}
