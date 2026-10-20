using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>MISSIONS.CLIENT.QUESTS_ES.QUEST_NAME.FIX.9 — Q.q name shape must match vanilla.</summary>
public sealed class QuestsEsQuestNameFix9Tests
{
    private static string Active1277 =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "content-client-publish",
            "quests_es_1277.swf");

    private static string Cached1275 =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "lang-cache",
            "quests_es_1275.swf");

    [Fact]
    public void Vanilla_Qq_single_stage_pushes_s_before_n()
    {
        if (!File.Exists(Active1277))
            return;

        var parsed = QuestsEsParser.ParseInternal(File.ReadAllBytes(Active1277));
        var snap = QuestsEsParser.ToSnapshot(parsed);
        var sampleId = snap.Quests.First(kv => kv.Key < 1000 && kv.Value.StageIds.Count == 1).Key;
        var ops = ReadQuestAssignOps(parsed, sampleId);
        Assert.True(ops.Count >= 6, "vanilla Q.q assign too short");
        // Push(id|s|stage0), Push(1), InitArr, Push(n), Push(name), Push(2), InitObj
        Assert.Equal(Avm1Opcode.Push, ops[0].Code);
        Assert.Equal(3, ops[0].PushItems!.Count);
        Assert.Equal(sampleId, ops[0].PushItems[0].IntValue);
        Assert.Equal(parsed.IdxS, ops[0].PushItems[1].ConstantIndex);
        Assert.Equal(snap.Quests[sampleId].StageIds[0], ops[0].PushItems[2].IntValue);
        Assert.Equal(Avm1Opcode.InitArray, ops[2].Code);
        Assert.Equal(Avm1Opcode.Push, ops[3].Code);
        Assert.Equal(parsed.IdxN, ops[3].PushItems![0].ConstantIndex);
        Assert.Equal(Avm1Opcode.Push, ops[4].Code);
        Assert.Equal(Avm1PushType.String, ops[4].PushItems![0].Type);
        Assert.Equal(snap.Quests[sampleId].Name, ops[4].PushItems[0].StringValue);
        Assert.Equal(Avm1Opcode.InitObject, ops[^2].Code);
        Assert.Equal(Avm1Opcode.SetMember, ops[^1].Code);
    }

    [Fact]
    public void Generated_PRUEBA_is_string_qq_like_3503()
    {
        var sourcePath = File.Exists(Cached1275) ? Cached1275
            : File.Exists(Active1277) ? Active1277 : null;
        if (sourcePath is null)
            return;

        var sourceBytes = File.ReadAllBytes(sourcePath);
        var before = QuestsEsParser.Parse(sourceBytes);
        var addition = new QuestsEsMissionAssignment
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

        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = sourceBytes,
            Additions = new[] { addition },
        });
        Assert.True(gen.Success, gen.Error);
        var outParsed = QuestsEsParser.ParseInternal(gen.OutputBytes!);
        var outSnap = QuestsEsParser.ToSnapshot(outParsed);

        Assert.Equal("PRUEBA", outSnap.Quests[100004].Name);
        Assert.True(outSnap.Quests[100004].IsStringName);
        Assert.Empty(outSnap.Quests[100004].StageIds);
        Assert.DoesNotContain("object Object", outSnap.Quests[100004].Name, StringComparison.OrdinalIgnoreCase);

        var pruebaOps = ReadQuestAssignOps(outParsed, 100004);
        Assert.Equal(Avm1Opcode.Push, pruebaOps[0].Code);
        Assert.Equal(1, pruebaOps[0].PushItems!.Count);
        Assert.Equal(100004, pruebaOps[0].PushItems[0].IntValue);
        Assert.Equal(Avm1Opcode.Push, pruebaOps[1].Code);
        Assert.Equal("PRUEBA", pruebaOps[1].PushItems![0].StringValue);
        Assert.Equal(Avm1Opcode.SetMember, pruebaOps[^1].Code);
        Assert.DoesNotContain(pruebaOps, o => o.Code == Avm1Opcode.InitObject);
        _ = before;
    }

    [Fact]
    public void Broken_1277_Qq_100004_had_n_before_s()
    {
        if (!File.Exists(Active1277))
            return;

        var parsed = QuestsEsParser.ParseInternal(File.ReadAllBytes(Active1277));
        var ops = ReadQuestAssignOps(parsed, 100004);
        // Legacy broken writer: Push(id), Push(n), Push(name), Push(s), ...
        Assert.True(ops.Count >= 4);
        Assert.Equal(Avm1Opcode.Push, ops[0].Code);
        Assert.True(ops[0].PushItems!.Count == 1 && ops[0].PushItems[0].IntValue == 100004);
        Assert.Equal(parsed.IdxN, ops[1].PushItems![0].ConstantIndex);
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
