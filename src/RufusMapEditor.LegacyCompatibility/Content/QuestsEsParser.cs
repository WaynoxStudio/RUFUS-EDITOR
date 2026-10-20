using System.Globalization;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Parse quests_es DoAction: Q.q / Q.s / Q.o / Q.t / VERSION / FILE_END.</summary>
public static class QuestsEsParser
{
    public const int DoActionTagCode = 12;

    public static QuestsEsSnapshot Parse(byte[] swfBytes) => ToSnapshot(ParseInternal(swfBytes));

    internal static QuestsEsParsed ParseInternal(byte[] swfBytes)
    {
        var container = SwfContainer.Read(swfBytes);
        var signature = container.WasCompressed ? "CWS" : "FWS";
        var doActionIndex = -1;
        var doActionCount = 0;
        for (var i = 0; i < container.Tags.Count; i++)
        {
            if (container.Tags[i].Code != DoActionTagCode)
                continue;
            doActionCount++;
            if (doActionIndex < 0)
                doActionIndex = i;
        }

        if (doActionCount == 0)
            throw new InvalidOperationException("quests_es: no se encontró DoAction.");
        if (doActionCount > 1)
            throw new InvalidOperationException("quests_es: más de un DoAction; abortado.");

        var actionData = container.Tags[doActionIndex].Data;
        var pool = Avm1Bytecode.ReadConstantPool(actionData, out var poolEnd);
        var actions = Avm1Bytecode.ParseActions(actionData, poolEnd, pool);
        var (version, versionOff) = FindVersion(actions, actionData);
        var fileEnd = FindFileEndPush(actions)
            ?? throw new InvalidOperationException("quests_es: no se encontró FILE_END.");

        var idxQ = RequirePool(pool, "Q");
        var idxQMap = RequirePool(pool, "q");
        var idxS = RequirePool(pool, "s");
        var idxO = RequirePool(pool, "o");
        var idxT = Avm1Bytecode.IndexOfName(pool, "t");
        var idxN = Avm1Bytecode.IndexOfName(pool, "n");
        var idxD = Avm1Bytecode.IndexOfName(pool, "d");
        var idxR = Avm1Bytecode.IndexOfName(pool, "r");
        var idxRbl = Avm1Bytecode.IndexOfName(pool, "rbl");
        var idxType = Avm1Bytecode.IndexOfName(pool, "t");
        var idxP = Avm1Bytecode.IndexOfName(pool, "p");
        var idxX = Avm1Bytecode.IndexOfName(pool, "x");
        var idxY = Avm1Bytecode.IndexOfName(pool, "y");

        ExtractMaps(
            actions,
            out var quests,
            out var stages,
            out var objectives,
            out var templates,
            out var questCount,
            out var stageCount,
            out var objectiveCount);

        var nonCanonical = QuestsEsQuestLayout.FindNonCanonicalQuestIds(
            actions, quests.Keys, idxQMap, idxS);

        return new QuestsEsParsed
        {
            Container = container,
            Signature = signature,
            DoActionTagIndex = doActionIndex,
            ActionData = actionData,
            ConstantPool = pool,
            Actions = actions,
            VersionValue = version,
            VersionIntOffset = versionOff,
            FileEndPushOffset = fileEnd.Offset,
            Quests = quests,
            Stages = stages,
            Objectives = objectives,
            TypeTemplates = templates,
            QuestAssignmentCount = questCount,
            StageAssignmentCount = stageCount,
            ObjectiveAssignmentCount = objectiveCount,
            NonCanonicalQuestIds = nonCanonical,
            IdxQ = idxQ,
            IdxQMap = idxQMap,
            IdxS = idxS,
            IdxO = idxO,
            IdxT = idxT ?? -1,
            IdxN = idxN ?? -1,
            IdxD = idxD ?? -1,
            IdxR = idxR ?? -1,
            IdxRbl = idxRbl ?? -1,
            IdxTypeProp = idxType ?? -1,
            IdxP = idxP ?? -1,
            IdxX = idxX ?? -1,
            IdxY = idxY ?? -1,
        };
    }

    internal static QuestsEsSnapshot ToSnapshot(QuestsEsParsed parsed) => new()
    {
        Version = parsed.VersionValue,
        SwfVersion = parsed.Container.Version,
        WasCompressed = parsed.Container.WasCompressed,
        Signature = parsed.Signature,
        Quests = parsed.Quests,
        Stages = parsed.Stages,
        Objectives = parsed.Objectives,
        TypeTemplates = parsed.TypeTemplates,
        QuestAssignmentCount = parsed.QuestAssignmentCount,
        StageAssignmentCount = parsed.StageAssignmentCount,
        ObjectiveAssignmentCount = parsed.ObjectiveAssignmentCount,
        HasFileEnd = true,
        ConstantPoolCount = parsed.ConstantPool.Count,
        DoActionCount = 1,
        NonCanonicalQuestIds = parsed.NonCanonicalQuestIds,
    };

    private static int RequirePool(IReadOnlyList<string> pool, string name) =>
        Avm1Bytecode.IndexOfName(pool, name)
        ?? throw new InvalidOperationException($"ConstantPool: falta '{name}'.");

    private static (int version, int intOffset) FindVersion(IReadOnlyList<Avm1Action> actions, byte[] actionData)
    {
        for (var i = 0; i < actions.Count - 2; i++)
        {
            var a = actions[i];
            if (a.Code != Avm1Opcode.Push || a.PushItems is null || a.PushItems.Count != 1)
                continue;
            if (!Avm1Bytecode.IsConstantNamed(a.PushItems[0], "VERSION")
                && a.PushItems[0].StringValue != "VERSION")
                continue;
            var b = actions[i + 1];
            if (b.Code != Avm1Opcode.Push || b.PushItems is null || b.PushItems.Count != 1
                || b.PushItems[0].Type != Avm1PushType.Integer)
                continue;
            if (actions[i + 2].Code != Avm1Opcode.SetVariable)
                continue;

            var intOffset = b.Offset + 3 + 1;
            var value = BitConverter.ToInt32(actionData, intOffset);
            if (value != b.PushItems[0].IntValue)
                throw new InvalidOperationException("VERSION: inconsistencia al leer entero.");
            return (value, intOffset);
        }

        throw new InvalidOperationException("VERSION no pudo localizarse en quests_es.");
    }

    private static Avm1Action? FindFileEndPush(IReadOnlyList<Avm1Action> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (a.Code != Avm1Opcode.Push || a.PushItems is null)
                continue;
            var has = a.PushItems.Any(p =>
                (p.Type == Avm1PushType.String && p.StringValue == "FILE_END")
                || Avm1Bytecode.IsConstantNamed(p, "FILE_END"));
            if (!has) continue;

            for (var j = i + 1; j < Math.Min(i + 4, actions.Count); j++)
            {
                if (actions[j].Code == Avm1Opcode.SetVariable)
                    return a;
            }
        }

        return null;
    }

    private static void ExtractMaps(
        IReadOnlyList<Avm1Action> actions,
        out Dictionary<int, QuestsEsQuestEntry> quests,
        out Dictionary<int, QuestsEsStageEntry> stages,
        out Dictionary<int, QuestsEsObjectiveEntry> objectives,
        out Dictionary<int, string> templates,
        out int questCount,
        out int stageCount,
        out int objectiveCount)
    {
        quests = new Dictionary<int, QuestsEsQuestEntry>();
        stages = new Dictionary<int, QuestsEsStageEntry>();
        objectives = new Dictionary<int, QuestsEsObjectiveEntry>();
        templates = new Dictionary<int, string>();
        questCount = 0;
        stageCount = 0;
        objectiveCount = 0;
        var stack = new List<StackItem>();

        void Push(StackItem v) => stack.Add(v);
        StackItem Pop()
        {
            if (stack.Count == 0) return StackItem.Unk;
            var v = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return v;
        }

        foreach (var act in actions)
        {
            switch (act.Code)
            {
                case Avm1Opcode.Push:
                    if (act.PushItems is null) break;
                    foreach (var p in act.PushItems)
                    {
                        if (p.Type == Avm1PushType.Null)
                            Push(StackItem.NullVal);
                        else if (p.Type == Avm1PushType.Integer && p.IntValue is int n)
                            Push(StackItem.Number(n));
                        else if (p.Type == Avm1PushType.String && p.StringValue is not null)
                            Push(StackItem.Text(p.StringValue));
                        else if (!string.IsNullOrEmpty(p.StringValue))
                            Push(StackItem.Text(p.StringValue));
                        else
                            Push(StackItem.Unk);
                    }
                    break;
                case Avm1Opcode.GetVariable:
                    Push(StackItem.Obj(Pop().AsName() ?? "?"));
                    break;
                case Avm1Opcode.GetMember:
                {
                    var name = Pop();
                    var obj = Pop();
                    var n = name.AsName() ?? name.Int?.ToString(CultureInfo.InvariantCulture) ?? "?";
                    Push(StackItem.Obj((obj.Path ?? obj.AsName() ?? "?") + "." + n));
                    break;
                }
                case Avm1Opcode.InitArray:
                {
                    var n = Pop().Int ?? 0;
                    var items = new List<object?>(n);
                    for (var i = 0; i < n; i++)
                        items.Add(Pop().ToValue());
                    items.Reverse();
                    Push(StackItem.Array(items));
                    break;
                }
                case Avm1Opcode.InitObject:
                {
                    var n = Pop().Int ?? 0;
                    var props = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (var i = 0; i < n; i++)
                    {
                        var val = Pop();
                        var key = Pop().AsName() ?? "?";
                        props[key] = val.ToValue();
                    }

                    Push(StackItem.Object(props));
                    break;
                }
                case Avm1Opcode.SetMember:
                {
                    var value = Pop();
                    var name = Pop();
                    var obj = Pop();
                    var path = obj.Path ?? obj.AsName() ?? "?";
                    var key = name.Int
                              ?? (int.TryParse(name.AsName(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                                  ? parsed
                                  : (int?)null);
                    if (key is not int id)
                        break;

                    if (path.Equals("Q.q", StringComparison.Ordinal) || path.EndsWith(".q", StringComparison.Ordinal))
                    {
                        // FIX.11 — functional custom quests use string: Q.q[id] = "name"
                        if (value.Props is null)
                        {
                            if (value.Str is null || value.IsNull) break;
                            questCount++;
                            quests[id] = new QuestsEsQuestEntry
                            {
                                Name = value.Str,
                                StageIds = Array.Empty<int>(),
                                IsStringName = true,
                            };
                            break;
                        }

                        value.Props.TryGetValue("n", out var qName);
                        value.Props.TryGetValue("s", out var stageList);
                        if (qName is not string qText) break;
                        questCount++;
                        quests[id] = new QuestsEsQuestEntry
                        {
                            Name = qText,
                            StageIds = ToIntList(stageList),
                            IsStringName = false,
                        };
                    }
                    else if (path.Equals("Q.s", StringComparison.Ordinal) || path.EndsWith(".s", StringComparison.Ordinal))
                    {
                        if (value.Props is null) break;
                        value.Props.TryGetValue("n", out var sName);
                        value.Props.TryGetValue("d", out var desc);
                        value.Props.TryGetValue("r", out var rewards);
                        value.Props.TryGetValue("rbl", out var rbl);
                        if (sName is not string sText) break;
                        stageCount++;
                        stages[id] = new QuestsEsStageEntry
                        {
                            Name = sText,
                            Description = desc ?? "",
                            Rewards = ToObjectList(rewards),
                            LevelBands = ToObjectList(rbl),
                        };
                    }
                    else if (path.Equals("Q.o", StringComparison.Ordinal) || path.EndsWith(".o", StringComparison.Ordinal))
                    {
                        if (value.Props is null) break;
                        value.Props.TryGetValue("t", out var tVal);
                        value.Props.TryGetValue("p", out var pVal);
                        value.Props.TryGetValue("x", out var xVal);
                        value.Props.TryGetValue("y", out var yVal);
                        if (tVal is not int t) break;
                        objectiveCount++;
                        objectives[id] = new QuestsEsObjectiveEntry
                        {
                            Type = t,
                            Parameters = ToObjectList(pVal),
                            X = xVal as int?,
                            Y = yVal as int?,
                        };
                    }
                    else if (path.Equals("Q.t", StringComparison.Ordinal) || path.EndsWith(".t", StringComparison.Ordinal))
                    {
                        if (value.Str is not null)
                            templates[id] = value.Str;
                    }

                    break;
                }
                case Avm1Opcode.SetVariable:
                    Pop();
                    Pop();
                    break;
                case Avm1Opcode.NewObject:
                {
                    var argc = Pop().Int ?? 0;
                    Pop();
                    for (var i = 0; i < argc; i++)
                        Pop();
                    Push(StackItem.Obj("new"));
                    break;
                }
            }
        }
    }

    private static IReadOnlyList<int> ToIntList(object? value)
    {
        if (value is not IList<object?> list)
            return Array.Empty<int>();
        var result = new List<int>(list.Count);
        foreach (var item in list)
        {
            if (item is int n)
                result.Add(n);
        }

        return result;
    }

    private static IReadOnlyList<object?> ToObjectList(object? value) =>
        value is IList<object?> list ? list.ToList() : Array.Empty<object?>();

    private readonly struct StackItem
    {
        public string? Path { get; init; }
        public string? Str { get; init; }
        public int? Int { get; init; }
        public bool IsNull { get; init; }
        public IReadOnlyDictionary<string, object?>? Props { get; init; }
        public IList<object?>? ArrayItems { get; init; }

        public static StackItem Unk => new() { Str = "?" };
        public static StackItem NullVal => new() { IsNull = true, Str = "null" };
        public static StackItem Obj(string path) => new() { Path = path, Str = path };
        public static StackItem Text(string s)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return new StackItem { Str = s, Int = n };
            return new StackItem { Str = s };
        }
        public static StackItem Number(int n) =>
            new() { Int = n, Str = n.ToString(CultureInfo.InvariantCulture) };
        public static StackItem Array(IList<object?> items) =>
            new() { ArrayItems = items, Str = "array" };
        public static StackItem Object(Dictionary<string, object?> props) =>
            new() { Props = props, Str = "object", Path = "object" };

        public string? AsName() => Path ?? Str;
        public object? ToValue()
        {
            if (IsNull) return null;
            if (ArrayItems is not null) return ArrayItems;
            if (Props is not null) return Props;
            if (Int is int n) return n;
            return Str;
        }
    }
}

internal sealed class QuestsEsParsed
{
    public required SwfContainer Container { get; init; }
    public required string Signature { get; init; }
    public required int DoActionTagIndex { get; init; }
    public required byte[] ActionData { get; init; }
    public required IReadOnlyList<string> ConstantPool { get; init; }
    public required IReadOnlyList<Avm1Action> Actions { get; init; }
    public required int VersionValue { get; init; }
    public required int VersionIntOffset { get; init; }
    public required int FileEndPushOffset { get; init; }
    public required IReadOnlyDictionary<int, QuestsEsQuestEntry> Quests { get; init; }
    public required IReadOnlyDictionary<int, QuestsEsStageEntry> Stages { get; init; }
    public required IReadOnlyDictionary<int, QuestsEsObjectiveEntry> Objectives { get; init; }
    public required IReadOnlyDictionary<int, string> TypeTemplates { get; init; }
    public required int QuestAssignmentCount { get; init; }
    public required int StageAssignmentCount { get; init; }
    public required int ObjectiveAssignmentCount { get; init; }
    public required int IdxQ { get; init; }
    public required int IdxQMap { get; init; }
    public required int IdxS { get; init; }
    public required int IdxO { get; init; }
    public required int IdxT { get; init; }
    public required int IdxN { get; init; }
    public required int IdxD { get; init; }
    public required int IdxR { get; init; }
    public required int IdxRbl { get; init; }
    public required int IdxTypeProp { get; init; }
    public required int IdxP { get; init; }
    public required int IdxX { get; init; }
    public required int IdxY { get; init; }
    public required IReadOnlySet<int> NonCanonicalQuestIds { get; init; }
}

/// <summary>
/// Detects Q.q AVM1 shapes:
/// - FIX.11 string name: Push(id); Push("name"); SetMember — valid for custom quests.
/// - Vanilla Ankama object: first Push packs (questId, "s", stages...).
/// - Legacy RUFUS object: Push(questId) alone then Push("n")... — needs repair when object form.
/// </summary>
public static class QuestsEsQuestLayout
{
    public static bool NeedsRepair(QuestsEsSnapshot snapshot, int questId) =>
        snapshot.NeedsQuestLayoutRepair(questId);

    /// <summary>
    /// Canonical object: first Push packs (questId, "s", stageIds...).
    /// Legacy object: Push(questId) alone then Push("n")...
    /// </summary>
    internal static bool IsCanonicalFirstPush(IReadOnlyList<Avm1PushItem>? items, int idxS)
    {
        if (items is null || items.Count < 2) return false;
        if (items[0].Type != Avm1PushType.Integer) return false;
        return items[1].ConstantIndex == idxS;
    }

    /// <summary>FIX.11 — <c>Q.q[id] = "name"</c> (no InitObject).</summary>
    internal static bool IsStringNameAssign(IReadOnlyList<Avm1Action> assignOps)
    {
        if (assignOps.Count < 3) return false;
        if (assignOps[^1].Code != Avm1Opcode.SetMember) return false;
        for (var i = 0; i < assignOps.Count; i++)
        {
            if (assignOps[i].Code == Avm1Opcode.InitObject)
                return false;
        }

        var first = assignOps[0];
        if (first.Code != Avm1Opcode.Push || first.PushItems is null || first.PushItems.Count != 1)
            return false;
        if (first.PushItems[0].Type != Avm1PushType.Integer)
            return false;

        var second = assignOps[1];
        if (second.Code != Avm1Opcode.Push || second.PushItems is null)
            return false;
        return second.PushItems.Any(x => x.Type == Avm1PushType.String);
    }

    internal static bool NeedsRepair(IReadOnlyList<Avm1Action> assignOps, int idxS)
    {
        if (assignOps.Count == 0) return true;
        if (IsStringNameAssign(assignOps)) return false;
        return !IsCanonicalFirstPush(assignOps[0].PushItems, idxS);
    }

    internal static IReadOnlySet<int> FindNonCanonicalQuestIds(
        IReadOnlyList<Avm1Action> actions,
        IEnumerable<int> questIds,
        int idxQMap,
        int idxS)
    {
        var bad = new HashSet<int>();
        foreach (var questId in questIds)
        {
            if (!TryReadLastQuestAssignOps(actions, idxQMap, questId, out var ops)
                || NeedsRepair(ops, idxS))
                bad.Add(questId);
        }

        return bad;
    }

    internal static bool TryReadLastQuestAssignOps(
        IReadOnlyList<Avm1Action> actions,
        int idxQMap,
        int questId,
        out List<Avm1Action> ops)
    {
        ops = new List<Avm1Action>();
        var found = false;
        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (a.Code != Avm1Opcode.Push || a.PushItems is null) continue;
            if (i < 3 || actions[i - 1].Code != Avm1Opcode.GetMember) continue;
            var prev = actions[i - 2].PushItems;
            if (prev is null || !prev.Any(x => x.ConstantIndex == idxQMap)) continue;
            if (!a.PushItems.Any(x => x.Type == Avm1PushType.Integer && x.IntValue == questId)) continue;
            if (a.PushItems.First(x => x.Type == Avm1PushType.Integer).IntValue != questId) continue;

            ops = new List<Avm1Action>();
            for (var j = i; j < actions.Count; j++)
            {
                ops.Add(actions[j]);
                if (actions[j].Code == Avm1Opcode.SetMember)
                    break;
            }

            found = true;
        }

        return found;
    }
}
