using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// CONT.7B.1 — append N.d[id] = { n, a? } + VERSION N+1.
/// Appends overwrite for updates (last SetMember wins). Does not rebuild ConstantPool.
/// </summary>
internal static class NpcEsMutator
{
    public static byte[] Apply(NpcEsParsed parsed, int newVersion, IReadOnlyList<NpcEsAssignment> additions)
    {
        if (newVersion != parsed.VersionValue + 1)
            throw new InvalidOperationException(
                $"VERSION npc_es debe ser N+1 ({parsed.VersionValue + 1}), recibido {newVersion}.");

        foreach (var add in additions)
        {
            DialogEsLatin1.Validate(add.Name, $"N.d[{add.Id}].n");
            foreach (var a in add.Actions)
            {
                if (!NpcEsClientActions.IsValid(a))
                    throw new InvalidOperationException($"Acción npc_es inválida: {a} (solo 1..8).");
            }
        }

        var data = (byte[])parsed.ActionData.Clone();
        BitConverter.TryWriteBytes(data.AsSpan(parsed.VersionIntOffset, 4), newVersion);

        if (additions.Count == 0)
            return data;

        using var ms = new MemoryStream();
        foreach (var add in additions)
            ms.Write(BuildAssignment(parsed, add));
        var block = ms.ToArray();

        var insertAt = parsed.FileEndPushOffset;
        var newData = new byte[data.Length + block.Length];
        Buffer.BlockCopy(data, 0, newData, 0, insertAt);
        Buffer.BlockCopy(block, 0, newData, insertAt, block.Length);
        Buffer.BlockCopy(data, insertAt, newData, insertAt + block.Length, data.Length - insertAt);
        return newData;
    }

    private static byte[] BuildAssignment(NpcEsParsed parsed, NpcEsAssignment add)
    {
        var actions = NpcEsClientActions.Normalize(add.Actions);
        if (actions.Count > 0 && parsed.IdxA < 0)
            throw new InvalidOperationException("ConstantPool: falta 'a' para emitir N.d[].a.");

        using var ms = new MemoryStream();
        void Raw(byte b) => ms.WriteByte(b);
        void Push(params Avm1PushItem[] items) => ms.Write(Avm1Bytecode.EncodePush(items));

        Push(Avm1PushItem.Constant(parsed.IdxN));
        Raw(Avm1Opcode.GetVariable);
        Push(Avm1PushItem.Constant(parsed.IdxD));
        Raw(Avm1Opcode.GetMember);

        Push(Avm1PushItem.Integer(add.Id));

        Push(Avm1PushItem.Constant(parsed.IdxNameProp));
        Push(Avm1PushItem.StringLiteral(add.Name));

        var propCount = 1;
        if (actions.Count > 0)
        {
            Push(Avm1PushItem.Constant(parsed.IdxA));
            for (var i = actions.Count - 1; i >= 0; i--)
                Push(Avm1PushItem.Integer(actions[i]));
            Push(Avm1PushItem.Integer(actions.Count));
            Raw(Avm1Opcode.InitArray);
            propCount = 2;
        }

        Push(Avm1PushItem.Integer(propCount));
        Raw(Avm1Opcode.InitObject);
        Raw(Avm1Opcode.SetMember);
        return ms.ToArray();
    }
}
