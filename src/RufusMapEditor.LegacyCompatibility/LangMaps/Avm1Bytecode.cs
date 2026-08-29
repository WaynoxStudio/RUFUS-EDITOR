using System.Text;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

internal sealed class Avm1PushItem
{
    public required Avm1PushType Type { get; init; }
    public string? StringValue { get; init; }
    public int? IntValue { get; init; }
    public bool? BoolValue { get; init; }
    public int? ConstantIndex { get; init; }

    public static Avm1PushItem Integer(int value) => new() { Type = Avm1PushType.Integer, IntValue = value };
    public static Avm1PushItem Constant(int index) => new()
    {
        Type = index <= 255 ? Avm1PushType.Constant8 : Avm1PushType.Constant16,
        ConstantIndex = index,
    };
    public static Avm1PushItem StringLiteral(string value) => new() { Type = Avm1PushType.String, StringValue = value };
    public static Avm1PushItem Boolean(bool value) => new() { Type = Avm1PushType.Boolean, BoolValue = value };
}

internal sealed class Avm1Action
{
    public required int Offset { get; init; }
    public required byte Code { get; init; }
    public required int Length { get; init; }
    public IReadOnlyList<Avm1PushItem>? PushItems { get; init; }
}

internal static class Avm1Bytecode
{
    public static IReadOnlyList<string> ReadConstantPool(ReadOnlySpan<byte> actionData, out int poolActionEnd)
    {
        if (actionData.Length < 3 || actionData[0] != Avm1Opcode.ConstantPool)
            throw new InvalidOperationException("AVM1: falta ActionConstantPool al inicio del DoAction.");

        var alen = BitConverter.ToUInt16(actionData.Slice(1, 2));
        var payload = actionData.Slice(3, alen);
        var count = BitConverter.ToUInt16(payload);
        var list = new List<string>(count);
        var pos = 2;
        for (var i = 0; i < count; i++)
        {
            var end = payload[pos..].IndexOf((byte)0);
            if (end < 0) throw new InvalidOperationException("AVM1: ConstantPool truncado.");
            list.Add(Encoding.Latin1.GetString(payload.Slice(pos, end)));
            pos += end + 1;
        }

        poolActionEnd = 3 + alen;
        return list;
    }

    public static int? IndexOfName(IReadOnlyList<string> pool, string name)
    {
        for (var i = 0; i < pool.Count; i++)
            if (string.Equals(pool[i], name, StringComparison.Ordinal))
                return i;
        return null;
    }

    public static bool IsConstantNamed(Avm1PushItem item, string name) =>
        item.Type is Avm1PushType.Constant8 or Avm1PushType.Constant16
        && string.Equals(item.StringValue, name, StringComparison.Ordinal);

    public static List<Avm1Action> ParseActions(ReadOnlySpan<byte> actionData, int startOffset, IReadOnlyList<string> pool)
    {
        var actions = new List<Avm1Action>();
        var pos = startOffset;
        while (pos < actionData.Length)
        {
            var code = actionData[pos];
            if (code == Avm1Opcode.End)
            {
                actions.Add(new Avm1Action { Offset = pos, Code = code, Length = 1 });
                break;
            }

            if (code < 0x80)
            {
                actions.Add(new Avm1Action { Offset = pos, Code = code, Length = 1 });
                pos += 1;
                continue;
            }

            var alen = BitConverter.ToUInt16(actionData.Slice(pos + 1, 2));
            var total = 3 + alen;
            IReadOnlyList<Avm1PushItem>? pushes = null;
            if (code == Avm1Opcode.Push)
                pushes = DecodePush(actionData.Slice(pos + 3, alen), pool);

            actions.Add(new Avm1Action { Offset = pos, Code = code, Length = total, PushItems = pushes });
            pos += total;
        }

        return actions;
    }

    public static List<Avm1PushItem> DecodePush(ReadOnlySpan<byte> payload, IReadOnlyList<string> pool)
    {
        var items = new List<Avm1PushItem>();
        var i = 0;
        while (i < payload.Length)
        {
            var t = (Avm1PushType)payload[i++];
            switch (t)
            {
                case Avm1PushType.String:
                {
                    var end = payload[i..].IndexOf((byte)0);
                    items.Add(Avm1PushItem.StringLiteral(Encoding.Latin1.GetString(payload.Slice(i, end))));
                    i += end + 1;
                    break;
                }
                case Avm1PushType.Float:
                    i += 4;
                    items.Add(new Avm1PushItem { Type = t });
                    break;
                case Avm1PushType.Null:
                case Avm1PushType.Undefined:
                    items.Add(new Avm1PushItem { Type = t });
                    break;
                case Avm1PushType.Register:
                    items.Add(new Avm1PushItem { Type = t, IntValue = payload[i++] });
                    break;
                case Avm1PushType.Boolean:
                    items.Add(Avm1PushItem.Boolean(payload[i++] != 0));
                    break;
                case Avm1PushType.Double:
                    i += 8;
                    items.Add(new Avm1PushItem { Type = t });
                    break;
                case Avm1PushType.Integer:
                {
                    var v = BitConverter.ToInt32(payload.Slice(i, 4));
                    i += 4;
                    items.Add(Avm1PushItem.Integer(v));
                    break;
                }
                case Avm1PushType.Constant8:
                {
                    var idx = payload[i++];
                    items.Add(new Avm1PushItem
                    {
                        Type = t,
                        ConstantIndex = idx,
                        StringValue = idx < pool.Count ? pool[idx] : null,
                    });
                    break;
                }
                case Avm1PushType.Constant16:
                {
                    var idx = BitConverter.ToUInt16(payload.Slice(i, 2));
                    i += 2;
                    items.Add(new Avm1PushItem
                    {
                        Type = t,
                        ConstantIndex = idx,
                        StringValue = idx < pool.Count ? pool[idx] : null,
                    });
                    break;
                }
                default:
                    throw new InvalidOperationException($"AVM1 Push: tipo desconocido {(byte)t}.");
            }
        }

        return items;
    }

    public static byte[] EncodePush(IReadOnlyList<Avm1PushItem> items)
    {
        using var ms = new MemoryStream();
        foreach (var item in items)
        {
            ms.WriteByte((byte)item.Type);
            switch (item.Type)
            {
                case Avm1PushType.String:
                    ms.Write(Encoding.Latin1.GetBytes(item.StringValue ?? ""));
                    ms.WriteByte(0);
                    break;
                case Avm1PushType.Boolean:
                    ms.WriteByte(item.BoolValue == true ? (byte)1 : (byte)0);
                    break;
                case Avm1PushType.Integer:
                    ms.Write(BitConverter.GetBytes(item.IntValue ?? 0));
                    break;
                case Avm1PushType.Constant8:
                    ms.WriteByte((byte)(item.ConstantIndex ?? 0));
                    break;
                case Avm1PushType.Constant16:
                    ms.Write(BitConverter.GetBytes((ushort)(item.ConstantIndex ?? 0)));
                    break;
                default:
                    throw new InvalidOperationException($"EncodePush no soporta {item.Type}.");
            }
        }

        var payload = ms.ToArray();
        var result = new byte[3 + payload.Length];
        result[0] = Avm1Opcode.Push;
        BitConverter.TryWriteBytes(result.AsSpan(1, 2), (ushort)payload.Length);
        payload.CopyTo(result.AsSpan(3));
        return result;
    }
}
