using System.Globalization;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.7B — parse npc_es DoAction: N.d[id].n / N.a / VERSION / FILE_END.</summary>
public static class NpcEsParser
{
    public const int DoActionTagCode = 12;

    public static NpcEsSnapshot Parse(byte[] swfBytes) => ToSnapshot(ParseInternal(swfBytes));

    internal static NpcEsParsed ParseInternal(byte[] swfBytes)
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
            throw new InvalidOperationException("npc_es: no se encontró DoAction.");
        if (doActionCount > 1)
            throw new InvalidOperationException("npc_es: más de un DoAction; abortado.");

        var actionData = container.Tags[doActionIndex].Data;
        var pool = Avm1Bytecode.ReadConstantPool(actionData, out var poolEnd);
        var actions = Avm1Bytecode.ParseActions(actionData, poolEnd, pool);
        var (version, versionOff) = FindVersion(actions, actionData);
        var fileEnd = FindFileEndPush(actions)
            ?? throw new InvalidOperationException("npc_es: no se encontró FILE_END.");

        var idxN = Avm1Bytecode.IndexOfName(pool, "N")
            ?? throw new InvalidOperationException("ConstantPool: falta 'N'.");
        var idxD = Avm1Bytecode.IndexOfName(pool, "d")
            ?? throw new InvalidOperationException("ConstantPool: falta 'd'.");
        var idxNameProp = Avm1Bytecode.IndexOfName(pool, "n")
            ?? throw new InvalidOperationException("ConstantPool: falta 'n'.");
        var idxA = Avm1Bytecode.IndexOfName(pool, "a")
            ?? throw new InvalidOperationException("ConstantPool: falta 'a'.");

        ExtractMaps(actions, out var names, out var npcActions, out var labels, out var nameCount, out var nameHits);

        return new NpcEsParsed
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
            Names = names,
            NpcActions = npcActions,
            ActionLabels = labels,
            NameAssignmentCount = nameCount,
            NameHits = nameHits,
            IdxN = idxN,
            IdxD = idxD,
            IdxNameProp = idxNameProp,
            IdxA = idxA,
        };
    }

    internal static NpcEsSnapshot ToSnapshot(NpcEsParsed parsed) => new()
    {
        Version = parsed.VersionValue,
        SwfVersion = parsed.Container.Version,
        WasCompressed = parsed.Container.WasCompressed,
        Signature = parsed.Signature,
        Names = parsed.Names,
        Actions = parsed.NpcActions.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<int>)kv.Value.ToList()),
        ActionLabels = parsed.ActionLabels,
        NameAssignmentCount = parsed.NameAssignmentCount,
        HasFileEnd = true,
        ConstantPoolCount = parsed.ConstantPool.Count,
        DoActionCount = 1,
    };

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

        throw new InvalidOperationException("VERSION no pudo localizarse en npc_es.");
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

            // FILE_END may be followed by Push(true) then SetVariable, or SetVariable directly.
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
        out Dictionary<int, string> names,
        out Dictionary<int, List<int>> npcActions,
        out Dictionary<int, string> labels,
        out int nameCount,
        out Dictionary<int, int> nameHits)
    {
        names = new Dictionary<int, string>();
        npcActions = new Dictionary<int, List<int>>();
        labels = new Dictionary<int, string>();
        nameHits = new Dictionary<int, int>();
        nameCount = 0;
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
                        if (p.Type == Avm1PushType.Integer && p.IntValue is int n)
                            Push(StackItem.Number(n));
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
                    var items = new List<int>(n);
                    for (var i = 0; i < n; i++)
                    {
                        var v = Pop();
                        if (v.Int is int iv)
                            items.Add(iv);
                    }

                    // AVM1: last popped = array[0] → items currently reversed relative to push order.
                    items.Reverse();
                    Push(StackItem.IntArray(NpcEsClientActions.Normalize(items).ToList()));
                    break;
                }
                case Avm1Opcode.InitObject:
                {
                    var n = Pop().Int ?? 0;
                    var props = new Dictionary<string, string>(StringComparer.Ordinal);
                    List<int>? actionIds = null;
                    for (var i = 0; i < n; i++)
                    {
                        var val = Pop();
                        var key = Pop().AsName() ?? "?";
                        if (key == "a" && val.IntList is not null)
                            actionIds = val.IntList.ToList();
                        else
                            props[key] = val.Str ?? val.AsName() ?? "";
                    }

                    props.TryGetValue("n", out var nameText);
                    Push(StackItem.Object(props, nameText, actionIds));
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

                    if (path.Equals("N.d", StringComparison.Ordinal)
                        || path.EndsWith(".d", StringComparison.Ordinal))
                    {
                        string? text = null;
                        if (value.Props is not null && value.Props.TryGetValue("n", out var nText))
                            text = nText;
                        else if (!string.IsNullOrEmpty(value.NameText))
                            text = value.NameText;
                        else if (value.Str is not null && value.Props is null)
                            text = value.Str;

                        if (text is null) break;
                        nameCount++;
                        nameHits[id] = nameHits.GetValueOrDefault(id) + 1;
                        names[id] = text;
                        if (value.ActionIds is { Count: > 0 })
                            npcActions[id] = NpcEsClientActions.Normalize(value.ActionIds).ToList();
                        else
                            npcActions[id] = new List<int>();
                    }
                    else if (path.Equals("N.a", StringComparison.Ordinal)
                             || path.EndsWith(".a", StringComparison.Ordinal))
                    {
                        if (value.Str is not null)
                            labels[id] = value.Str;
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

    private readonly struct StackItem
    {
        public string? Path { get; init; }
        public string? Str { get; init; }
        public int? Int { get; init; }
        public IReadOnlyList<int>? IntList { get; init; }
        public IReadOnlyDictionary<string, string>? Props { get; init; }
        public string? NameText { get; init; }
        public IReadOnlyList<int>? ActionIds { get; init; }

        public static StackItem Unk => new() { Str = "?" };
        public static StackItem Obj(string path) => new() { Path = path, Str = path };
        public static StackItem Text(string s)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return new StackItem { Str = s, Int = n };
            return new StackItem { Str = s };
        }
        public static StackItem Number(int n) =>
            new() { Int = n, Str = n.ToString(CultureInfo.InvariantCulture) };
        public static StackItem IntArray(IReadOnlyList<int> items) =>
            new() { IntList = items, Str = "array" };
        public static StackItem Object(
            Dictionary<string, string> props,
            string? nameText,
            IReadOnlyList<int>? actionIds) =>
            new()
            {
                Props = props,
                NameText = nameText,
                ActionIds = actionIds,
                Str = nameText ?? "object",
                Path = "object",
            };
        public string? AsName() => Path ?? Str;
    }
}

internal sealed class NpcEsParsed
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
    public required Dictionary<int, string> Names { get; init; }
    public required Dictionary<int, List<int>> NpcActions { get; init; }
    public required Dictionary<int, string> ActionLabels { get; init; }
    public required int NameAssignmentCount { get; init; }
    public required Dictionary<int, int> NameHits { get; init; }
    public required int IdxN { get; init; }
    public required int IdxD { get; init; }
    public required int IdxNameProp { get; init; }
    public required int IdxA { get; init; }
}
