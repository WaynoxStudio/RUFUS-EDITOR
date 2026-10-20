using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>MISSIONS.CLIENT.QUESTS_ES.QUEST_NAME.REPAIR_DETECTION.FIX.10</summary>
public sealed class QuestsEsRepairDetectionFix10Tests
{
    private static string Active1277 =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "content-client-publish",
            "quests_es_1277.swf");

    private static QuestsEsMissionAssignment PruebaAssignment() =>
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

    [Fact]
    public void NeedsRepair_true_for_legacy_n_before_s_first_push()
    {
        var legacy = new List<Avm1Action>
        {
            new()
            {
                Offset = 0,
                Code = Avm1Opcode.Push,
                Length = 0,
                PushItems = new[] { Avm1PushItem.Integer(100004) },
            },
        };
        Assert.True(QuestsEsQuestLayout.NeedsRepair(legacy, idxS: 3));
    }

    [Fact]
    public void NeedsRepair_false_for_vanilla_s_in_first_push()
    {
        var vanilla = new List<Avm1Action>
        {
            new()
            {
                Offset = 0,
                Code = Avm1Opcode.Push,
                Length = 0,
                PushItems = new[]
                {
                    Avm1PushItem.Integer(100004),
                    Avm1PushItem.Constant(3),
                    Avm1PushItem.Integer(5501),
                },
            },
        };
        Assert.False(QuestsEsQuestLayout.NeedsRepair(vanilla, idxS: 3));
    }

    [Fact]
    public void Same_semantic_legacy_vs_vanilla_only_legacy_NeedsRepair()
    {
        var legacyBytes = BuildLegacyPruebaSwf(40);
        var vanillaBytes = QuestsEsSeed.Create(40, new[] { PruebaAssignment() });

        var legacySnap = QuestsEsParser.Parse(legacyBytes);
        var vanillaSnap = QuestsEsParser.Parse(vanillaBytes);

        Assert.Equal("PRUEBA", legacySnap.Quests[100004].Name);
        Assert.Equal(new[] { 5501 }, legacySnap.Quests[100004].StageIds.ToArray());
        Assert.False(legacySnap.Quests[100004].IsStringName);
        Assert.Equal("PRUEBA", vanillaSnap.Quests[100004].Name);
        Assert.True(vanillaSnap.Quests[100004].IsStringName);
        Assert.Empty(vanillaSnap.Quests[100004].StageIds);

        Assert.True(QuestsEsQuestLayout.NeedsRepair(legacySnap, 100004));
        Assert.False(QuestsEsQuestLayout.NeedsRepair(vanillaSnap, 100004));
        Assert.False(QuestsEsPublishBatchBuilder.MissionMatchesSnapshot(PruebaAssignment(), legacySnap));
        Assert.True(QuestsEsPublishBatchBuilder.MissionMatchesSnapshot(PruebaAssignment(), vanillaSnap));
        Assert.True(QuestsEsPublishBatchBuilder.NeedsStringFormRepair(legacySnap, 100004));
        Assert.False(QuestsEsPublishBatchBuilder.NeedsStringFormRepair(vanillaSnap, 100004));
    }

    [Fact]
    public void PublishedClient_legacy_layout_still_pending_repair_Qq_only()
    {
        var legacyBytes = BuildLegacyPruebaSwf(40);
        var snap = QuestsEsParser.Parse(legacyBytes);
        Assert.Equal(40, snap.Version);
        Assert.True(snap.NeedsQuestLayoutRepair(100004));

        var ws = PublishedPruebaWorkspace(questsEsVersion: 40);
        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);

        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.Equal(1, batch.NewMissionCount);
        Assert.Equal(41, batch.TargetVersion);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.Equal("replace", b.Kind);
        Assert.Equal("Q.q objeto → string", b.RepairReason);
        Assert.Empty(b.Assignment!.Stages);
        Assert.Empty(b.Assignment.Objectives);
        Assert.Equal(100004, b.Assignment.Quest.QuestId);
        Assert.True(b.Assignment.WriteQuest);
        Assert.Contains("Q.q objeto → string", batch.FormatPreview(), StringComparison.Ordinal);
        Assert.Contains("ETAPAS / Q.s", batch.FormatPreview(), StringComparison.Ordinal);
        Assert.Contains("SIN CAMBIOS", batch.FormatPreview(), StringComparison.Ordinal);
        Assert.DoesNotContain("Q.s[5501]", batch.FormatPreview(), StringComparison.Ordinal);
        Assert.DoesNotContain("Q.o[4661]", batch.FormatPreview(), StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_repair_generate_bumps_N_plus_1_without_touching_matching_So()
    {
        var legacyBytes = BuildLegacyPruebaSwf(40);
        var before = QuestsEsParser.Parse(legacyBytes);
        var stageR = QuestsEsCodec.FormatRewards(before.Stages[5501].Rewards);
        var objP = QuestsEsCodec.FormatValue(before.Objectives[4661].Parameters);

        var assignment = new QuestsEsMissionAssignment
        {
            Quest = PruebaAssignment().Quest,
            Stages = Array.Empty<QuestsEsStageAssignment>(),
            Objectives = Array.Empty<QuestsEsObjectiveAssignment>(),
        };

        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = legacyBytes,
            Additions = new[] { assignment },
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(40, gen.SourceVersion);
        Assert.Equal(41, gen.TargetVersion);

        var after = gen.OutputSnapshot!;
        Assert.False(after.NeedsQuestLayoutRepair(100004));
        Assert.Equal("PRUEBA", after.Quests[100004].Name);
        Assert.Equal(stageR, QuestsEsCodec.FormatRewards(after.Stages[5501].Rewards));
        Assert.Equal(objP, QuestsEsCodec.FormatValue(after.Objectives[4661].Parameters));
        Assert.Equal(before.Stages[5501].Name, after.Stages[5501].Name);
        Assert.Equal(before.Objectives[4661].Type, after.Objectives[4661].Type);
    }

    [Fact]
    public void Active_1277_Qq_100004_marked_for_layout_repair_when_present()
    {
        if (!File.Exists(Active1277))
            return;

        var snap = QuestsEsParser.Parse(File.ReadAllBytes(Active1277));
        Assert.Equal(1277, snap.Version);
        Assert.Equal("PRUEBA", snap.Quests[100004].Name);
        Assert.Equal(new[] { 5501 }, snap.Quests[100004].StageIds.ToArray());
        Assert.True(QuestsEsQuestLayout.NeedsRepair(snap, 100004));

        var ws = PublishedPruebaWorkspace(questsEsVersion: 1277);
        // Align stage/obj to remote so only layout differs.
        AlignWorkspaceToRemote(ws, snap);

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        Assert.Equal(1, batch.NewMissionCount);
        Assert.Equal(1278, batch.TargetVersion);
        var b = Assert.Single(batch.Bindings, x => x.Assignment is not null);
        Assert.Equal("replace", b.Kind);
        Assert.Equal("Q.q objeto → string", b.RepairReason);
        Assert.Empty(b.Assignment!.Stages);
        Assert.Empty(b.Assignment.Objectives);
    }

    private static ContentDraftWorkspace PublishedPruebaWorkspace(int questsEsVersion)
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.QuestsEsPublished = true;
        mission.QuestsEsPublishedVersion = questsEsVersion;
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
            Detalle = "",
        });
        return ws;
    }

    private static void AlignWorkspaceToRemote(ContentDraftWorkspace ws, QuestsEsSnapshot snap)
    {
        var mission = ws.Missions.Missions[0];
        var remoteSt = snap.Stages[5501];
        mission.Stages[0].Nombre = remoteSt.Name;
        mission.Stages[0].Descripcion = remoteSt.Description as string ?? mission.Stages[0].Descripcion;
        // Rebuild rewards so EncodeRewards matches remote slots (item order Ankama).
        mission.Stages[0].Rewards = DecodeRewardSlots(remoteSt.Rewards);

        var remoteO = snap.Objectives[4661];
        mission.Stages[0].Objectives[0].Tipo = remoteO.Type;
        if (remoteO.Parameters.Count == 1 && remoteO.Parameters[0] is int mapId)
            mission.Stages[0].Objectives[0].Args = mapId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        else if (remoteO.Parameters.Count == 1 && remoteO.Parameters[0] is long mapL)
            mission.Stages[0].Objectives[0].Args = mapL.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static MissionRewardsDraft DecodeRewardSlots(IReadOnlyList<object?> rewards)
    {
        var d = new MissionRewardsDraft();
        // EncodeRewards: [oficios, hechizos, emotes, objetos[[qty,id]], kamas, exp]
        if (rewards.Count >= 6)
        {
            d.Kamas = AsInt(rewards[4]) ?? 0;
            d.Exp = AsInt(rewards[5]) ?? 0;
            if (rewards[3] is IList<object?> items)
            {
                foreach (var entry in items)
                {
                    if (entry is not IList<object?> pair || pair.Count < 2) continue;
                    var qty = AsInt(pair[0]);
                    var id = AsInt(pair[1]);
                    if (qty is null || id is null) continue;
                    d.Objetos.Add(new MissionRewardItem { ItemId = id.Value, Cantidad = qty.Value });
                }
            }

            if (rewards[2] is string em) d.Emotes = em;
            if (rewards[1] is string he) d.Hechizos = he;
            if (rewards[0] is string of) d.Oficios = of;
        }

        return d;
    }

    private static int? AsInt(object? v) =>
        v switch
        {
            int i => i,
            long l => (int)l,
            _ => null,
        };

    private static byte[] BuildLegacyPruebaSwf(int version)
    {
        if (version <= 1)
            throw new ArgumentOutOfRangeException(nameof(version));

        var empty = QuestsEsSeed.Create(version - 1);
        var parsed = QuestsEsParser.ParseInternal(empty);
        var action = QuestsEsMutator.ApplyWithLegacyQuest(parsed, version, PruebaAssignment());
        return parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, action).Write(compress: true);
    }
}
