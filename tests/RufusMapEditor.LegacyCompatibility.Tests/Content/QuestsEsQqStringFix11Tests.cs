using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>MISSIONS.CLIENT.QUESTS_ES.QQ_STRING.FIX.11 — Q.q is string name (like 3503).</summary>
public sealed class QuestsEsQqStringFix11Tests
{
    private static string Active1279 =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "content-client-publish",
            "quests_es_1279.swf");

    private static string Cached1275 =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "lang-cache",
            "quests_es_1275.swf");

    [Fact]
    public void Real_3503_parses_as_string_name()
    {
        if (!File.Exists(Active1279))
            return;

        var snap = QuestsEsParser.Parse(File.ReadAllBytes(Active1279));
        Assert.True(snap.Quests.TryGetValue(3503, out var q));
        Assert.True(q.IsStringName);
        Assert.StartsWith("Pieles Mojadas", q.Name, StringComparison.Ordinal);
        Assert.Empty(q.StageIds);
        Assert.False(snap.NeedsQuestLayoutRepair(3503));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Real_vanilla_low_id_still_object_form(int questId)
    {
        if (!File.Exists(Active1279))
            return;

        var snap = QuestsEsParser.Parse(File.ReadAllBytes(Active1279));
        Assert.True(snap.Quests.TryGetValue(questId, out var q));
        Assert.False(q.IsStringName);
        Assert.False(string.IsNullOrWhiteSpace(q.Name));
        Assert.NotEmpty(q.StageIds);
    }

    [Fact]
    public void Generated_rufus_quest_is_string_not_object()
    {
        var sourcePath = File.Exists(Cached1275) ? Cached1275
            : File.Exists(Active1279) ? Active1279 : null;
        if (sourcePath is null)
            return;

        var addition = new QuestsEsMissionAssignment
        {
            Quest = new QuestsEsQuestAssignment
            {
                QuestId = 100005,
                Name = "Misioneros al poder",
                StageIds = new[] { 5502 },
            },
            Stages =
            [
                new QuestsEsStageAssignment
                {
                    StageId = 5502,
                    Name = "Misiones faciles",
                    Description = "Esto es una prueba.",
                    Rewards = new MissionRewardsDraft { Exp = 200000, Kamas = 20000 },
                },
            ],
            Objectives =
            [
                new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = 4662,
                    Type = MissionObjectiveTypes.DiscoverMap,
                    Args = "10306",
                    Detalle = "",
                },
            ],
        };

        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = File.ReadAllBytes(sourcePath),
            Additions = new[] { addition },
        });
        Assert.True(gen.Success, gen.Error);
        var outSnap = gen.OutputSnapshot!;
        Assert.True(outSnap.Quests[100005].IsStringName);
        Assert.Equal("Misioneros al poder", outSnap.Quests[100005].Name);
        Assert.Empty(outSnap.Quests[100005].StageIds);
        Assert.True(outSnap.Stages.ContainsKey(5502));
        Assert.True(outSnap.Objectives.ContainsKey(4662));

        var ops = ReadQuestAssignOps(QuestsEsParser.ParseInternal(gen.OutputBytes!), 100005);
        Assert.Equal(Avm1Opcode.Push, ops[0].Code);
        Assert.Equal(1, ops[0].PushItems!.Count);
        Assert.Equal(100005, ops[0].PushItems[0].IntValue);
        Assert.Equal(Avm1Opcode.Push, ops[1].Code);
        Assert.Equal(Avm1PushType.String, ops[1].PushItems![0].Type);
        Assert.Equal("Misioneros al poder", ops[1].PushItems[0].StringValue);
        Assert.Equal(Avm1Opcode.SetMember, ops[^1].Code);
        Assert.DoesNotContain(ops, o => o.Code == Avm1Opcode.InitObject);
    }

    [Fact]
    public void Preview_shows_string_qq_and_bd_etapas()
    {
        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(10));
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Misioneros al poder";
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100005;
        mission.QuestsEsPublished = false;
        mission.Stages.Add(new MissionStageDraft
        {
            Id = 5502,
            Nombre = "Misiones faciles",
            Descripcion = "x",
            Rewards = new MissionRewardsDraft { Exp = 1, Kamas = 1 },
        });
        mission.Stages[0].Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4662,
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "10306",
            Detalle = "",
        });

        var batch = QuestsEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("\n", batch.Errors));
        var preview = batch.FormatPreview();
        Assert.Contains("Q.q[100005] = \"Misioneros al poder\";", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Q.q[100005] = {", preview, StringComparison.Ordinal);
        Assert.Contains("Q.s[5502]", preview, StringComparison.Ordinal);
        Assert.Contains("ETAPAS / Q.s", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Object_form_needs_string_repair()
    {
        var seed = QuestsEsSeed.Create(20);
        var parsed = QuestsEsParser.ParseInternal(seed);
        var action = QuestsEsMutator.ApplyWithObjectQuest(
            parsed,
            parsed.VersionValue + 1,
            new QuestsEsMissionAssignment
            {
                Quest = new QuestsEsQuestAssignment
                {
                    QuestId = 100005,
                    Name = "Misioneros al poder",
                    StageIds = new[] { 5502 },
                },
                Stages =
                [
                    new QuestsEsStageAssignment
                    {
                        StageId = 5502,
                        Name = "Misiones faciles",
                        Description = "x",
                        Rewards = new MissionRewardsDraft(),
                    },
                ],
                Objectives = Array.Empty<QuestsEsObjectiveAssignment>(),
            });
        var swf = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, action).Write(compress: true);
        var snap = QuestsEsParser.Parse(swf);
        Assert.False(snap.Quests[100005].IsStringName);
        Assert.Equal(new[] { 5502 }, snap.Quests[100005].StageIds.ToArray());
        Assert.True(QuestsEsPublishBatchBuilder.NeedsStringFormRepair(snap, 100005));
        Assert.True(QuestsEsPublishBatchBuilder.NeedsQuestObjectRepair(snap, 100005));
    }

    private static List<Avm1Action> ReadQuestAssignOps(QuestsEsParsed parsed, int questId)
    {
        for (var i = 0; i < parsed.Actions.Count; i++)
        {
            var a = parsed.Actions[i];
            if (a.Code != Avm1Opcode.Push || a.PushItems is null) continue;
            if (i < 3 || parsed.Actions[i - 1].Code != Avm1Opcode.GetMember) continue;
            var prev = parsed.Actions[i - 2].PushItems;
            if (prev is null || !prev.Any(x => x.ConstantIndex == parsed.IdxQMap)) continue;
            if (!a.PushItems.Any(x => x.Type == Avm1PushType.Integer && x.IntValue == questId)) continue;
            if (a.PushItems.First(x => x.Type == Avm1PushType.Integer).IntValue != questId) continue;

            var ops = new List<Avm1Action>();
            for (var j = i; j < parsed.Actions.Count; j++)
            {
                ops.Add(parsed.Actions[j]);
                if (parsed.Actions[j].Code == Avm1Opcode.SetMember)
                    break;
            }

            return ops;
        }

        throw new InvalidOperationException($"Q.q[{questId}] assign not found.");
    }
}
