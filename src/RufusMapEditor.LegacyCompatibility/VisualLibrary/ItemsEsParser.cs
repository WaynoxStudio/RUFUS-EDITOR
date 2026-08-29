using System.Globalization;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// LIB.2 — parse items_es DoAction: I[id] = { n, t, g, l, ... } and type labels.
/// </summary>
public static class ItemsEsParser
{
    public const int DoActionTagCode = 12;

    public static ItemsEsSnapshot Parse(byte[] swfBytes)
    {
        var container = SwfContainer.Read(swfBytes);
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
            throw new InvalidOperationException("items_es: no se encontró DoAction.");

        var actionData = container.Tags[doActionIndex].Data;
        var pool = Avm1Bytecode.ReadConstantPool(actionData, out var poolEnd);
        var actions = Avm1Bytecode.ParseActions(actionData, poolEnd, pool);

        Extract(actions, out var items, out var types, out var version);

        return new ItemsEsSnapshot
        {
            Version = version,
            Items = items,
            TypeNames = types,
        };
    }

    private static void Extract(
        IReadOnlyList<Avm1Action> actions,
        out Dictionary<int, ItemsEsRawItem> items,
        out Dictionary<int, string> types,
        out int? version)
    {
        items = new Dictionary<int, ItemsEsRawItem>();
        types = new Dictionary<int, string>();
        version = null;
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
                        else if (p.Type == Avm1PushType.Boolean && p.BoolValue is bool b)
                            Push(StackItem.Number(b ? 1 : 0));
                        else if (!string.IsNullOrEmpty(p.StringValue))
                            Push(StackItem.Text(p.StringValue!));
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
                    var arr = new List<string>(n);
                    for (var i = 0; i < n; i++)
                    {
                        var v = Pop();
                        arr.Add(v.Str ?? v.AsName() ?? v.Int?.ToString(CultureInfo.InvariantCulture) ?? "");
                    }

                    arr.Reverse();
                    Push(StackItem.StringArray(arr));
                    break;
                }
                case Avm1Opcode.InitObject:
                {
                    var n = Pop().Int ?? 0;
                    var props = new Dictionary<string, StackItem>(StringComparer.Ordinal);
                    for (var i = 0; i < n; i++)
                    {
                        var val = Pop();
                        var key = Pop().AsName() ?? "?";
                        props[key] = val;
                    }

                    Push(StackItem.Object(props));
                    break;
                }
                case Avm1Opcode.SetVariable:
                {
                    var value = Pop();
                    var name = Pop().AsName() ?? "";
                    if (name.Equals("VERSION", StringComparison.Ordinal) && value.Int is int ver)
                        version = ver;
                    // Type catalog: often assigned as array to a short name; capture string arrays of type labels.
                    if (value.StrList is { Count: > 0 } labels
                        && labels.Count >= 5
                        && labels.Any(s => s.Contains("Sombrero", StringComparison.OrdinalIgnoreCase)
                                           || s.Contains("Capa", StringComparison.OrdinalIgnoreCase)))
                    {
                        for (var i = 0; i < labels.Count; i++)
                        {
                            if (!string.IsNullOrWhiteSpace(labels[i]))
                                types[i] = labels[i];
                        }
                    }
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

                    // I[id] or I.u[id] / I.*[id] = { n,t,g,l,... }
                    if (key is int id
                        && value.Props is not null
                        && LooksLikeItemObject(value.Props)
                        && IsItemContainerPath(path))
                    {
                        var nombre = ReadStringProp(value.Props, "n") ?? "";
                        var typeId = ReadIntProp(value.Props, "t") ?? 0;
                        var gfx = ReadIntProp(value.Props, "g") ?? 0;
                        var level = ReadIntProp(value.Props, "l") ?? 0;
                        items[id] = new ItemsEsRawItem
                        {
                            ItemId = id,
                            Nombre = nombre,
                            TypeId = typeId,
                            GfxId = gfx,
                            Level = level,
                        };
                    }

                    // Type map: T[id] = "Name" or similar
                    if (key is int typeKey
                        && (path.Equals("T", StringComparison.Ordinal)
                            || path.EndsWith(".T", StringComparison.Ordinal)
                            || path.Equals("IT", StringComparison.Ordinal))
                        && !string.IsNullOrWhiteSpace(value.Str ?? value.AsName()))
                    {
                        types[typeKey] = value.Str ?? value.AsName() ?? "";
                    }

                    break;
                }
                case Avm1Opcode.CallMethod:
                case Avm1Opcode.NewMethod:
                case Avm1Opcode.CallFunction:
                case Avm1Opcode.NewObject:
                {
                    var argc = Pop().Int ?? 0;
                    for (var i = 0; i < argc; i++)
                        Pop();
                    Pop(); // method / function / class name
                    if (act.Code is Avm1Opcode.CallMethod or Avm1Opcode.NewMethod)
                        Pop(); // object
                    Push(StackItem.Unk);
                    break;
                }
                case Avm1Opcode.Pop:
                    Pop();
                    break;
                case Avm1Opcode.Duplicate:
                    if (stack.Count > 0)
                        Push(stack[^1]);
                    break;
                case Avm1Opcode.Swap:
                    if (stack.Count >= 2)
                    {
                        var a = Pop();
                        var b = Pop();
                        Push(a);
                        Push(b);
                    }
                    break;
                default:
                    // Best-effort: ignore unknown ops that don't touch our maps.
                    break;
            }
        }
    }

    private static bool IsItemContainerPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.Equals("I", StringComparison.Ordinal)) return true;
        if (path.StartsWith("I.", StringComparison.Ordinal)) return true;
        // Some builds use short aliases; accept any path that ends with .u / .us when props look like items.
        if (path.EndsWith(".u", StringComparison.Ordinal) || path.EndsWith(".us", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static bool LooksLikeItemObject(Dictionary<string, StackItem> props) =>
        props.ContainsKey("n") && (props.ContainsKey("g") || props.ContainsKey("t") || props.ContainsKey("l"));

    private static string? ReadStringProp(Dictionary<string, StackItem> props, string key)
    {
        if (!props.TryGetValue(key, out var v))
            return null;
        return v.Str ?? v.AsName();
    }

    private static int? ReadIntProp(Dictionary<string, StackItem> props, string key)
    {
        if (!props.TryGetValue(key, out var v))
            return null;
        if (v.Int is int i)
            return i;
        if (int.TryParse(v.Str ?? v.AsName(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
            return p;
        return null;
    }

    private readonly struct StackItem
    {
        public int? Int { get; init; }
        public string? Str { get; init; }
        public string? Path { get; init; }
        public Dictionary<string, StackItem>? Props { get; init; }
        public IReadOnlyList<string>? StrList { get; init; }

        public static StackItem Unk => default;
        public static StackItem Number(int n) => new() { Int = n };
        public static StackItem Text(string s) => new() { Str = s };
        public static StackItem Obj(string path) => new() { Path = path };
        public static StackItem Object(Dictionary<string, StackItem> props) => new() { Props = props };
        public static StackItem StringArray(List<string> list) => new() { StrList = list };

        public string? AsName() => Str ?? Path;
    }
}

public sealed class ItemsEsRawItem
{
    public required int ItemId { get; init; }
    public required string Nombre { get; init; }
    public required int TypeId { get; init; }
    public required int GfxId { get; init; }
    public required int Level { get; init; }
}

public sealed class ItemsEsSnapshot
{
    public int? Version { get; init; }
    public required IReadOnlyDictionary<int, ItemsEsRawItem> Items { get; init; }
    public required IReadOnlyDictionary<int, string> TypeNames { get; init; }
}
