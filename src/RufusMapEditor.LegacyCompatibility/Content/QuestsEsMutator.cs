using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Append Q.q / Q.s / Q.o before FILE_END and bump VERSION N+1.</summary>
internal static class QuestsEsMutator
{
    public static byte[] Apply(QuestsEsParsed parsed, int newVersion, IReadOnlyList<QuestsEsMissionAssignment> additions)
    {
        if (newVersion != parsed.VersionValue + 1)
            throw new InvalidOperationException(
                $"VERSION quests_es debe ser N+1 ({parsed.VersionValue + 1}), recibido {newVersion}.");

        var data = (byte[])parsed.ActionData.Clone();
        BitConverter.TryWriteBytes(data.AsSpan(parsed.VersionIntOffset, 4), newVersion);

        if (additions.Count == 0)
            return data;

        using var ms = new MemoryStream();
        foreach (var mission in additions)
        {
            foreach (var obj in mission.Objectives)
                ms.Write(BuildObjective(parsed, obj));
            foreach (var stage in mission.Stages)
                ms.Write(BuildStage(parsed, stage));
            if (mission.WriteQuest)
                ms.Write(BuildQuest(parsed, mission.Quest));
        }

        var block = ms.ToArray();
        var insertAt = parsed.FileEndPushOffset;
        var newData = new byte[data.Length + block.Length];
        Buffer.BlockCopy(data, 0, newData, 0, insertAt);
        Buffer.BlockCopy(block, 0, newData, insertAt, block.Length);
        Buffer.BlockCopy(data, insertAt, newData, insertAt + block.Length, data.Length - insertAt);
        return newData;
    }

    private static byte[] BuildQuest(QuestsEsParsed parsed, QuestsEsQuestAssignment quest)
    {
        // FIX.11 — match functional custom Q.q[3503] = "name" (string), not {n,s}.
        // Quest→stages live in BD misiones.etapas; Q.s/Q.o stay separate.
        var name = DialogEsLatin1.Prepare(quest.Name, $"Q.q[{quest.QuestId}]");
        using var ms = new MemoryStream();
        var w = new Writer(ms, parsed);
        w.GetMemberQ("q");
        w.PushInteger(quest.QuestId);
        w.PushString(name);
        w.SetMember();
        return ms.ToArray();
    }

    /// <summary>Pre-FIX.11 object Q.q {n,s}. Tests only — must never ship to production.</summary>
    internal static byte[] BuildQuestObjectForm(QuestsEsParsed parsed, QuestsEsQuestAssignment quest)
    {
        var name = DialogEsLatin1.Prepare(quest.Name, $"Q.q[{quest.QuestId}].n");
        using var ms = new MemoryStream();
        var w = new Writer(ms, parsed);
        w.GetMemberQ("q");
        w.WriteQuestStagesProperty(quest.QuestId, quest.StageIds);
        w.PushProp("n", parsed.IdxN);
        w.PushString(name);
        w.InitObject(2);
        w.SetMember();
        return ms.ToArray();
    }

    /// <summary>Pre-FIX.9 Q.q shape (n before s). Tests only — must never ship to production.</summary>
    internal static byte[] BuildQuestLegacyBroken(QuestsEsParsed parsed, QuestsEsQuestAssignment quest)
    {
        var name = DialogEsLatin1.Prepare(quest.Name, $"Q.q[{quest.QuestId}].n");
        using var ms = new MemoryStream();
        var w = new Writer(ms, parsed);
        w.GetMemberQ("q");
        w.PushInteger(quest.QuestId);
        w.PushProp("n", parsed.IdxN);
        w.PushString(name);
        w.PushProp("s", parsed.IdxS);
        w.WriteStageIdArray(quest.StageIds);
        w.InitObject(2);
        w.SetMember();
        return ms.ToArray();
    }

    /// <summary>Seed a SWF whose Q.q uses legacy n-before-s layout (semantic n/s still parse).</summary>
    internal static byte[] ApplyWithLegacyQuest(
        QuestsEsParsed parsed,
        int newVersion,
        QuestsEsMissionAssignment mission)
    {
        if (newVersion != parsed.VersionValue + 1)
            throw new InvalidOperationException(
                $"VERSION quests_es debe ser N+1 ({parsed.VersionValue + 1}), recibido {newVersion}.");

        var data = (byte[])parsed.ActionData.Clone();
        BitConverter.TryWriteBytes(data.AsSpan(parsed.VersionIntOffset, 4), newVersion);

        using var ms = new MemoryStream();
        foreach (var obj in mission.Objectives)
            ms.Write(BuildObjective(parsed, obj));
        foreach (var stage in mission.Stages)
            ms.Write(BuildStage(parsed, stage));
        ms.Write(BuildQuestLegacyBroken(parsed, mission.Quest));

        var block = ms.ToArray();
        var insertAt = parsed.FileEndPushOffset;
        var newData = new byte[data.Length + block.Length];
        Buffer.BlockCopy(data, 0, newData, 0, insertAt);
        Buffer.BlockCopy(block, 0, newData, insertAt, block.Length);
        Buffer.BlockCopy(data, insertAt, newData, insertAt + block.Length, data.Length - insertAt);
        return newData;
    }

    /// <summary>Seed object-form Q.q {s,n} (pre-FIX.11). Tests only.</summary>
    internal static byte[] ApplyWithObjectQuest(
        QuestsEsParsed parsed,
        int newVersion,
        QuestsEsMissionAssignment mission)
    {
        if (newVersion != parsed.VersionValue + 1)
            throw new InvalidOperationException(
                $"VERSION quests_es debe ser N+1 ({parsed.VersionValue + 1}), recibido {newVersion}.");

        var data = (byte[])parsed.ActionData.Clone();
        BitConverter.TryWriteBytes(data.AsSpan(parsed.VersionIntOffset, 4), newVersion);

        using var ms = new MemoryStream();
        foreach (var obj in mission.Objectives)
            ms.Write(BuildObjective(parsed, obj));
        foreach (var stage in mission.Stages)
            ms.Write(BuildStage(parsed, stage));
        ms.Write(BuildQuestObjectForm(parsed, mission.Quest));

        var block = ms.ToArray();
        var insertAt = parsed.FileEndPushOffset;
        var newData = new byte[data.Length + block.Length];
        Buffer.BlockCopy(data, 0, newData, 0, insertAt);
        Buffer.BlockCopy(block, 0, newData, insertAt, block.Length);
        Buffer.BlockCopy(data, insertAt, newData, insertAt + block.Length, data.Length - insertAt);
        return newData;
    }

    private static byte[] BuildStage(QuestsEsParsed parsed, QuestsEsStageAssignment stage)
    {
        var name = DialogEsLatin1.Prepare(stage.Name, $"Q.s[{stage.StageId}].n");
        var desc = QuestsEsCodec.EncodeStageDescription(stage.Description);
        using var ms = new MemoryStream();
        var w = new Writer(ms, parsed);
        w.GetMemberQ("s");
        w.PushInteger(stage.StageId);
        w.PushProp("n", parsed.IdxN);
        w.PushString(name);
        w.PushProp("d", parsed.IdxD);
        if (desc is int di)
            w.PushInteger(di);
        else
            w.PushString((string)desc);
        w.PushProp("r", parsed.IdxR);
        w.WriteRewardArray(QuestsEsCodec.EncodeRewards(stage.Rewards));
        w.PushProp("rbl", parsed.IdxRbl);
        w.WriteEmptyArray();
        w.InitObject(4);
        w.SetMember();
        return ms.ToArray();
    }

    private static byte[] BuildObjective(QuestsEsParsed parsed, QuestsEsObjectiveAssignment objective)
    {
        var draft = new MissionObjectiveDraft
        {
            Id = objective.ObjectiveId,
            Tipo = objective.Type,
            Args = objective.Args,
            Detalle = objective.Detalle,
        };
        var p = QuestsEsCodec.EncodeObjectiveParameters(draft);
        var (x, y) = QuestsEsCodec.ReadObjectiveCoords(draft);
        var propCount = 2 + (x is not null ? 1 : 0) + (y is not null ? 1 : 0);

        using var ms = new MemoryStream();
        var w = new Writer(ms, parsed);
        w.GetMemberQ("o");
        w.PushInteger(objective.ObjectiveId);
        w.PushProp("t", parsed.IdxTypeProp);
        w.PushInteger(objective.Type);
        w.PushProp("p", parsed.IdxP);
        w.WriteParameterArray(p);
        if (x is int xv)
        {
            w.PushProp("x", parsed.IdxX);
            w.PushInteger(xv);
        }

        if (y is int yv)
        {
            w.PushProp("y", parsed.IdxY);
            w.PushInteger(yv);
        }

        w.InitObject(propCount);
        w.SetMember();
        return ms.ToArray();
    }

    private sealed class Writer
    {
        private readonly MemoryStream _ms;
        private readonly QuestsEsParsed _parsed;

        public Writer(MemoryStream ms, QuestsEsParsed parsed)
        {
            _ms = ms;
            _parsed = parsed;
        }

        public void GetMemberQ(string mapName)
        {
            PushConstant(_parsed.IdxQ);
            Raw(Avm1Opcode.GetVariable);
            PushConstant(mapName switch
            {
                "q" => _parsed.IdxQMap,
                "s" => _parsed.IdxS,
                "o" => _parsed.IdxO,
                _ => throw new InvalidOperationException($"Mapa Q.{mapName} desconocido."),
            });
            Raw(Avm1Opcode.GetMember);
        }

        public void PushConstant(int idx) => _ms.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(idx) }));
        public void PushInteger(int value) => _ms.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(value) }));
        public void PushString(string value) => _ms.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.StringLiteral(value) }));
        public void PushNull() => _ms.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Null() }));

        /// <summary>
        /// Property name: use ConstantPool when present; otherwise StringLiteral.
        /// Production quests_es omits p/x/y from the pool and uses string literals.
        /// </summary>
        public void PushProp(string name, int poolIdx)
        {
            if (poolIdx >= 0)
                PushConstant(poolIdx);
            else
                PushString(name);
        }

        public void InitObject(int propCount)
        {
            PushInteger(propCount);
            Raw(Avm1Opcode.InitObject);
        }
        public void SetMember() => Raw(Avm1Opcode.SetMember);
        public void Raw(byte b) => _ms.WriteByte(b);

        public void WriteEmptyArray()
        {
            PushInteger(0);
            Raw(Avm1Opcode.InitArray);
        }

        public void WriteStageIdArray(IReadOnlyList<int> ids)
        {
            // AVM1 InitArray: push elements in order [0]..[n-1], then count (Ankama/Flash).
            for (var i = 0; i < ids.Count; i++)
                PushInteger(ids[i]);
            PushInteger(ids.Count);
            Raw(Avm1Opcode.InitArray);
        }

        /// <summary>
        /// Q.q[id] stages property exactly as Ankama encodes it:
        /// Push(questId, "s", stageIds[0]...); Push(count); InitArray
        /// leaving (questId, "s", array) on the stack for InitObject.
        /// </summary>
        public void WriteQuestStagesProperty(int questId, IReadOnlyList<int> stageIds)
        {
            ArgumentNullException.ThrowIfNull(stageIds);
            if (_parsed.IdxS < 0)
                throw new InvalidOperationException("ConstantPool sin 's' para Q.q.");

            if (stageIds.Count == 0)
            {
                _ms.Write(Avm1Bytecode.EncodePush(new[]
                {
                    Avm1PushItem.Integer(questId),
                    Avm1PushItem.Constant(_parsed.IdxS),
                }));
                PushInteger(0);
                Raw(Avm1Opcode.InitArray);
                return;
            }

            var first = new List<Avm1PushItem>(3)
            {
                Avm1PushItem.Integer(questId),
                Avm1PushItem.Constant(_parsed.IdxS),
                Avm1PushItem.Integer(stageIds[0]),
            };
            _ms.Write(Avm1Bytecode.EncodePush(first));
            for (var i = 1; i < stageIds.Count; i++)
                PushInteger(stageIds[i]);
            PushInteger(stageIds.Count);
            Raw(Avm1Opcode.InitArray);
        }

        public void WriteRewardArray(object?[] slots)
        {
            for (var i = 0; i < slots.Length; i++)
                WriteValue(slots[i]);
            PushInteger(slots.Length);
            Raw(Avm1Opcode.InitArray);
        }

        public void WriteParameterArray(object?[] items)
        {
            for (var i = 0; i < items.Length; i++)
                WriteValue(items[i]);
            PushInteger(items.Length);
            Raw(Avm1Opcode.InitArray);
        }

        private void WriteValue(object? value)
        {
            switch (value)
            {
                case null:
                    PushNull();
                    break;
                case int n:
                    PushInteger(n);
                    break;
                case string s:
                    PushString(DialogEsLatin1.Prepare(s));
                    break;
                case int[] pair:
                    for (var i = 0; i < pair.Length; i++)
                        PushInteger(pair[i]);
                    PushInteger(pair.Length);
                    Raw(Avm1Opcode.InitArray);
                    break;
                case object[] nested:
                    for (var i = 0; i < nested.Length; i++)
                        WriteValue(nested[i]);
                    PushInteger(nested.Length);
                    Raw(Avm1Opcode.InitArray);
                    break;
                default:
                    PushString(value.ToString() ?? "");
                    break;
            }
        }
    }
}
