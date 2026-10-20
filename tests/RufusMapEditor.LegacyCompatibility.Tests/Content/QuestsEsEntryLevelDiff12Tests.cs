using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>MISSIONS.CLIENT.QUESTS_ES.ENTRY_LEVEL_DIFF.12</summary>
public sealed class QuestsEsEntryLevelDiff12Tests
{
    private static QuestsEsMissionAssignment BaseMission(
        string questName = "Misioneros al poder",
        string stageName = "Misiones faciles",
        string stageDesc = "Esto es una prueba.",
        int exp = 200000,
        int kamas = 20000,
        string mapArgs = "10306") =>
        new()
        {
            Quest = new QuestsEsQuestAssignment
            {
                QuestId = 100005,
                Name = questName,
                StageIds = new[] { 5502 },
            },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 5502,
                    Name = stageName,
                    Description = stageDesc,
                    Rewards = new MissionRewardsDraft { Exp = exp, Kamas = kamas },
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4662,
                    Type = MissionObjectiveTypes.DiscoverMap,
                    Args = mapArgs,
                    Detalle = "",
                },
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4663,
                    Type = MissionObjectiveTypes.DiscoverMap,
                    Args = "10307",
                    Detalle = "",
                },
            ],
        };

    private static byte[] SeedSwf(QuestsEsMissionAssignment mission, int version = 40)
    {
        var empty = QuestsEsSeed.Create(version - 1);
        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = empty,
            Additions = new[] { mission },
        });
        Assert.True(gen.Success, gen.Error);
        return gen.OutputBytes!;
    }

    private static ContentDraftWorkspace WorkspaceFrom(QuestsEsMissionAssignment mission, int version)
    {
        var ws = new ContentDraftWorkspace();
        var m = ws.Missions.CreateMission();
        m.PublishedBd = true;
        m.PublishedQuestId = mission.Quest.QuestId;
        m.QuestsEsPublished = true;
        m.QuestsEsPublishedVersion = version;
        m.Nombre = mission.Quest.Name;
        foreach (var st in mission.Stages)
        {
            var stage = new MissionStageDraft
            {
                Id = st.StageId,
                Nombre = st.Name,
                Descripcion = st.Description,
                Rewards = st.Rewards.Clone(),
            };
            foreach (var o in mission.Objectives)
            {
                // Only attach objectives that belong with the single stage in fixtures.
                stage.Objectives.Add(new MissionObjectiveDraft
                {
                    Id = o.ObjectiveId,
                    Tipo = o.Type,
                    Args = o.Args,
                    Detalle = o.Detalle,
                });
            }

            m.Stages.Add(stage);
        }

        return ws;
    }

    [Fact]
    public void Title_only_change_writes_only_Qq()
    {
        var seed = SeedSwf(BaseMission());
        var snap = QuestsEsParser.Parse(seed);
        var ws = WorkspaceFrom(BaseMission(questName: "Misioneros al poder"), snap.Version);
        ws.Missions.Missions[0].Nombre = "Titulo nuevo";

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.True(b.Assignment!.WriteQuest);
        Assert.Empty(b.Assignment.Stages);
        Assert.Empty(b.Assignment.Objectives);
        Assert.Equal("Titulo nuevo", b.Assignment.Quest.Name);

        var preview = batch.FormatPreview();
        Assert.Contains("Q.q[100005] = \"Titulo nuevo\";", preview, StringComparison.Ordinal);
        Assert.Contains("ETAPAS / Q.s", preview, StringComparison.Ordinal);
        Assert.Contains("OBJETIVOS / Q.o", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Q.s[5502]", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Q.o[4662]", preview, StringComparison.Ordinal);

        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = seed,
            Additions = batch.Additions,
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(snap.Version + 1, gen.TargetVersion);
        Assert.Equal("Titulo nuevo", gen.OutputSnapshot!.Quests[100005].Name);
        Assert.Equal(snap.Stages[5502].Name, gen.OutputSnapshot.Stages[5502].Name);
        Assert.Equal(snap.Objectives[4662].Type, gen.OutputSnapshot.Objectives[4662].Type);
    }

    [Fact]
    public void Identical_Qs_not_written()
    {
        var seed = SeedSwf(BaseMission());
        var snap = QuestsEsParser.Parse(seed);
        var ws = WorkspaceFrom(BaseMission(), snap.Version);
        ws.Missions.Missions[0].Nombre = "Otro titulo";

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.Empty(b.Assignment!.Stages);
    }

    [Fact]
    public void Identical_Qo_not_written()
    {
        var seed = SeedSwf(BaseMission());
        var snap = QuestsEsParser.Parse(seed);
        var ws = WorkspaceFrom(BaseMission(), snap.Version);
        ws.Missions.Missions[0].Nombre = "Otro titulo";

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.Empty(b.Assignment!.Objectives);
    }

    [Fact]
    public void Stage_name_change_writes_only_that_Qs()
    {
        var seed = SeedSwf(BaseMission());
        var snap = QuestsEsParser.Parse(seed);
        var ws = WorkspaceFrom(BaseMission(), snap.Version);
        ws.Missions.Missions[0].Stages[0].Nombre = "Etapa cambiada";

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.False(b.Assignment!.WriteQuest);
        Assert.Single(b.Assignment.Stages);
        Assert.Equal(5502, b.Assignment.Stages[0].StageId);
        Assert.Empty(b.Assignment.Objectives);
        Assert.Contains("Q.s[5502]", batch.FormatPreview(), StringComparison.Ordinal);
        Assert.DoesNotContain("Q.q[100005]", batch.FormatPreview(), StringComparison.Ordinal);
    }

    [Fact]
    public void Objective_change_writes_only_that_Qo()
    {
        var seed = SeedSwf(BaseMission());
        var snap = QuestsEsParser.Parse(seed);
        var ws = WorkspaceFrom(BaseMission(), snap.Version);
        ws.Missions.Missions[0].Stages[0].Objectives[0].Args = "99999";

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.False(b.Assignment!.WriteQuest);
        Assert.Empty(b.Assignment.Stages);
        Assert.Single(b.Assignment.Objectives);
        Assert.Equal(4662, b.Assignment.Objectives[0].ObjectiveId);
        Assert.DoesNotContain("Q.o[4663]", batch.FormatPreview(), StringComparison.Ordinal);
    }

    [Fact]
    public void Title_and_stage_change_writes_Qq_and_Qs()
    {
        var seed = SeedSwf(BaseMission());
        var snap = QuestsEsParser.Parse(seed);
        var ws = WorkspaceFrom(BaseMission(), snap.Version);
        ws.Missions.Missions[0].Nombre = "Nuevo";
        ws.Missions.Missions[0].Stages[0].Nombre = "Etapa nueva";

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.True(b.Assignment!.WriteQuest);
        Assert.Single(b.Assignment.Stages);
        Assert.Empty(b.Assignment.Objectives);
        var preview = batch.FormatPreview();
        Assert.Contains("Q.q[100005]", preview, StringComparison.Ordinal);
        Assert.Contains("Q.s[5502]", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Object_to_string_repair_writes_only_Qq()
    {
        var empty = QuestsEsSeed.Create(39);
        var parsed = QuestsEsParser.ParseInternal(empty);
        var action = QuestsEsMutator.ApplyWithObjectQuest(parsed, 40, BaseMission());
        var seed = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, action).Write(compress: true);
        var snap = QuestsEsParser.Parse(seed);
        Assert.True(QuestsEsPublishBatchBuilder.NeedsStringFormRepair(snap, 100005));

        var ws = WorkspaceFrom(BaseMission(), snap.Version);
        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.True(b.Assignment!.WriteQuest);
        Assert.Empty(b.Assignment.Stages);
        Assert.Empty(b.Assignment.Objectives);
        Assert.Equal("Q.q objeto → string", b.RepairReason);
    }

    [Fact]
    public void Repair_plus_rename_is_single_Qq_write()
    {
        var empty = QuestsEsSeed.Create(39);
        var parsed = QuestsEsParser.ParseInternal(empty);
        var action = QuestsEsMutator.ApplyWithObjectQuest(parsed, 40, BaseMission());
        var seed = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, action).Write(compress: true);
        var snap = QuestsEsParser.Parse(seed);

        var ws = WorkspaceFrom(BaseMission(questName: "Final unico"), snap.Version);
        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.True(b.Assignment!.WriteQuest);
        Assert.Equal("Final unico", b.Assignment.Quest.Name);
        Assert.Empty(b.Assignment.Stages);
        Assert.Empty(b.Assignment.Objectives);

        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = seed,
            Additions = batch.Additions,
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(1, gen.TargetVersion - gen.SourceVersion);
        Assert.True(gen.OutputSnapshot!.Quests[100005].IsStringName);
        Assert.Equal("Final unico", gen.OutputSnapshot.Quests[100005].Name);
    }

    [Fact]
    public void No_real_diff_means_quests_es_sin_cambios()
    {
        var seed = SeedSwf(BaseMission());
        var snap = QuestsEsParser.Parse(seed);
        var ws = WorkspaceFrom(BaseMission(), snap.Version);

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.Empty(batch.Additions);
        Assert.Equal(0, batch.NewMissionCount);
        var preview = batch.FormatPreview();
        Assert.Contains("Actual: " + snap.Version, preview, StringComparison.Ordinal);
        Assert.Contains("Previsto nuevo: " + snap.Version, preview, StringComparison.Ordinal);
        Assert.Contains("SIN CAMBIOS", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_entry_changes_single_swf_version_bump()
    {
        var seed = SeedSwf(BaseMission());
        var snap = QuestsEsParser.Parse(seed);
        var ws = WorkspaceFrom(BaseMission(), snap.Version);
        ws.Missions.Missions[0].Nombre = "A";
        ws.Missions.Missions[0].Stages[0].Nombre = "B";
        ws.Missions.Missions[0].Stages[0].Objectives[0].Args = "1";

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.Equal(snap.Version + 1, batch.TargetVersion);
        Assert.Single(batch.Additions);

        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = seed,
            Additions = batch.Additions,
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(snap.Version + 1, gen.TargetVersion);
    }
}
