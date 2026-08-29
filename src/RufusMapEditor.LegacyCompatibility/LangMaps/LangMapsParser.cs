namespace RufusMapEditor.LegacyCompatibility.LangMaps;

internal static class LangMapsParser
{
    public const int DoActionTagCode = 12;

    public static LangMapsParsed Parse(byte[] swfBytes)
    {
        var container = SwfContainer.Read(swfBytes);
        var doActionIndex = -1;
        for (var i = 0; i < container.Tags.Count; i++)
        {
            if (container.Tags[i].Code != DoActionTagCode)
                continue;
            if (doActionIndex >= 0)
                throw new InvalidOperationException("SWF maps_es: más de un DoAction; abortado.");
            doActionIndex = i;
        }

        if (doActionIndex < 0)
            throw new InvalidOperationException("SWF maps_es: no se encontró tag DoAction.");

        var actionData = container.Tags[doActionIndex].Data;
        var pool = Avm1Bytecode.ReadConstantPool(actionData, out var poolEnd);
        var idxMa = RequireConst(pool, "MA");
        var idxM = RequireConst(pool, "m");
        var idxX = RequireConst(pool, "x");
        var idxY = RequireConst(pool, "y");
        var idxSa = RequireConst(pool, "sa");
        var idxEp = RequireConst(pool, "ep");
        var idxVersion = RequireConst(pool, "VERSION");

        var actions = Avm1Bytecode.ParseActions(actionData, poolEnd, pool);
        var (version, versionIntOffset) = FindVersion(actions, actionData);
        var fileEndPush = FindFileEndPush(actions)
            ?? throw new InvalidOperationException(
                "AVM1: no se encontró el marcador FILE_END (literal o ConstantPool).");

        return new LangMapsParsed
        {
            Container = container,
            DoActionTagIndex = doActionIndex,
            ActionData = actionData,
            ConstantPool = pool,
            PoolEnd = poolEnd,
            Actions = actions,
            VersionValue = version,
            VersionIntOffset = versionIntOffset,
            FileEndPushOffset = fileEndPush.Offset,
            Entries = FindMaEntries(actions, actionData),
            IdxMa = idxMa,
            IdxM = idxM,
            IdxX = idxX,
            IdxY = idxY,
            IdxSa = idxSa,
            IdxEp = idxEp,
            IdxVersion = idxVersion,
        };
    }

    public static IReadOnlyList<LangMapEntry> ToPublicEntries(LangMapsParsed parsed) =>
        parsed.Entries.Select(e =>
        {
            var extras = new Dictionary<string, object?>();
            foreach (var (k, v) in e.StringProps)
                extras[k] = v;
            foreach (var (k, v) in e.IntProps)
            {
                if (k is "x" or "y" or "sa" or "ep")
                    continue;
                extras[k] = v;
            }

            return new LangMapEntry
            {
                MapId = e.MapId,
                X = e.IntProps.GetValueOrDefault("x"),
                Y = e.IntProps.GetValueOrDefault("y"),
                SubArea = e.IntProps.GetValueOrDefault("sa"),
                Ep = e.IntProps.GetValueOrDefault("ep"),
                ExtraProperties = extras,
            };
        }).ToList();

    private static int RequireConst(IReadOnlyList<string> pool, string name) =>
        Avm1Bytecode.IndexOfName(pool, name)
        ?? throw new InvalidOperationException($"ConstantPool: falta constante requerida '{name}'.");

    private static (int version, int intOffset) FindVersion(IReadOnlyList<Avm1Action> actions, byte[] actionData)
    {
        for (var i = 0; i < actions.Count - 2; i++)
        {
            var a = actions[i];
            if (a.Code != Avm1Opcode.Push || a.PushItems is null || a.PushItems.Count != 1)
                continue;
            if (!Avm1Bytecode.IsConstantNamed(a.PushItems[0], "VERSION"))
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

        throw new InvalidOperationException("VERSION no pudo localizarse inequívocamente en AVM1.");
    }

    private static Avm1Action? FindFileEndPush(IReadOnlyList<Avm1Action> actions)
    {
        for (var i = 0; i < actions.Count - 1; i++)
        {
            var a = actions[i];
            if (a.Code != Avm1Opcode.Push || a.PushItems is null)
                continue;
            var has = a.PushItems.Any(p =>
                (p.Type == Avm1PushType.String && p.StringValue == "FILE_END")
                || Avm1Bytecode.IsConstantNamed(p, "FILE_END"));
            if (has && actions[i + 1].Code == Avm1Opcode.SetVariable)
                return a;
        }

        return null;
    }

    private static List<LangMaEntrySpan> FindMaEntries(IReadOnlyList<Avm1Action> actions, byte[] actionData)
    {
        var result = new List<LangMaEntrySpan>();
        var i = 0;
        while (i < actions.Count - 6)
        {
            if (!IsMaPrefix(actions, i))
            {
                i++;
                continue;
            }

            var start = actions[i].Offset;
            var j = i + 4;
            var pushActions = new List<Avm1Action>();
            while (j < actions.Count && actions[j].Code == Avm1Opcode.Push)
            {
                pushActions.Add(actions[j]);
                j++;
            }

            if (j >= actions.Count || actions[j].Code != Avm1Opcode.InitObject
                || j + 1 >= actions.Count || actions[j + 1].Code != Avm1Opcode.SetMember)
            {
                i++;
                continue;
            }

            var tracked = TrackPushItems(pushActions, actionData);
            if (tracked.Count < 3
                || tracked[0].Item.Type != Avm1PushType.Integer
                || tracked[^1].Item.Type != Avm1PushType.Integer)
            {
                i++;
                continue;
            }

            var mapId = tracked[0].Item.IntValue!.Value;
            var nprops = tracked[^1].Item.IntValue!.Value;
            var pairs = tracked.Skip(1).Take(tracked.Count - 2).ToList();
            if (pairs.Count != nprops * 2)
            {
                i++;
                continue;
            }

            var intProps = new Dictionary<string, int>(StringComparer.Ordinal);
            var stringProps = new Dictionary<string, string>(StringComparer.Ordinal);
            var intOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
            var ok = true;
            for (var p = 0; p < nprops; p++)
            {
                var name = pairs[p * 2].Item.StringValue;
                var valueSlot = pairs[p * 2 + 1];
                if (string.IsNullOrEmpty(name))
                {
                    ok = false;
                    break;
                }

                if (valueSlot.Item.Type == Avm1PushType.Integer)
                {
                    intProps[name] = valueSlot.Item.IntValue!.Value;
                    if (valueSlot.IntPayloadOffset is int off)
                        intOffsets[name] = off;
                }
                else if (valueSlot.Item.Type is Avm1PushType.String or Avm1PushType.Constant8 or Avm1PushType.Constant16)
                {
                    stringProps[name] = valueSlot.Item.StringValue ?? "";
                }
            }

            if (!ok)
            {
                i++;
                continue;
            }

            result.Add(new LangMaEntrySpan
            {
                MapId = mapId,
                ActionStart = start,
                ActionEnd = actions[j + 1].Offset + actions[j + 1].Length,
                NProps = nprops,
                IntProps = intProps,
                StringProps = stringProps,
                IntValueOffsets = intOffsets,
            });
            i = j + 2;
        }

        return result;
    }

    private static bool IsMaPrefix(IReadOnlyList<Avm1Action> actions, int i)
    {
        if (i + 3 >= actions.Count) return false;
        var p0 = actions[i];
        if (p0.Code != Avm1Opcode.Push || p0.PushItems is null || p0.PushItems.Count != 1) return false;
        if (!Avm1Bytecode.IsConstantNamed(p0.PushItems[0], "MA")) return false;
        if (actions[i + 1].Code != Avm1Opcode.GetVariable) return false;
        var p2 = actions[i + 2];
        if (p2.Code != Avm1Opcode.Push || p2.PushItems is null || p2.PushItems.Count != 1) return false;
        if (!Avm1Bytecode.IsConstantNamed(p2.PushItems[0], "m")) return false;
        return actions[i + 3].Code == Avm1Opcode.GetMember;
    }

    private readonly record struct TrackedPush(Avm1PushItem Item, int? IntPayloadOffset);

    private static List<TrackedPush> TrackPushItems(IReadOnlyList<Avm1Action> pushActions, byte[] actionData)
    {
        var list = new List<TrackedPush>();
        foreach (var push in pushActions)
        {
            var payloadStart = push.Offset + 3;
            var payload = actionData.AsSpan(payloadStart, push.Length - 3);
            var decoded = push.PushItems!;
            var di = 0;
            var i = 0;
            while (i < payload.Length && di < decoded.Count)
            {
                var t = (Avm1PushType)payload[i++];
                int? intOff = null;
                switch (t)
                {
                    case Avm1PushType.Integer:
                        intOff = payloadStart + i;
                        i += 4;
                        break;
                    case Avm1PushType.Constant8:
                        i += 1;
                        break;
                    case Avm1PushType.Constant16:
                        i += 2;
                        break;
                    case Avm1PushType.String:
                        i += payload[i..].IndexOf((byte)0) + 1;
                        break;
                    case Avm1PushType.Boolean:
                        i += 1;
                        break;
                    case Avm1PushType.Float:
                        i += 4;
                        break;
                    case Avm1PushType.Double:
                        i += 8;
                        break;
                    case Avm1PushType.Register:
                        i += 1;
                        break;
                    case Avm1PushType.Null:
                    case Avm1PushType.Undefined:
                        break;
                    default:
                        throw new InvalidOperationException($"Push type {(byte)t} no soportado en MA.m.");
                }

                list.Add(new TrackedPush(decoded[di++], intOff));
            }
        }

        return list;
    }
}
