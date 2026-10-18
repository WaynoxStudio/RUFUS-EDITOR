using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// CONT.6B — append-only D.q / D.a assignments + VERSION N+1.
/// Does not rebuild ConstantPool. New texts are Push string literals (SWF6 UTF-8).
/// </summary>
internal static class DialogEsMutator
{
    public static byte[] Apply(DialogEsParsed parsed, int newVersion, IReadOnlyList<DialogEsAssignment> additions)
    {
        if (newVersion != parsed.VersionValue + 1)
            throw new InvalidOperationException(
                $"VERSION dialog_es debe ser N+1 ({parsed.VersionValue + 1}), recibido {newVersion}.");

        var prepared = new List<(DialogEsAssignment Add, string Text)>(additions.Count);
        foreach (var add in additions)
        {
            var label = add.Space == DialogEsSpace.Question ? "D.q" : "D.a";
            prepared.Add((add, DialogEsLatin1.Prepare(add.Text, label)));
        }

        var data = (byte[])parsed.ActionData.Clone();
        BitConverter.TryWriteBytes(data.AsSpan(parsed.VersionIntOffset, 4), newVersion);

        if (prepared.Count == 0)
            return data;

        using var ms = new MemoryStream();
        foreach (var (add, text) in prepared)
            ms.Write(BuildAssignment(parsed, add, text));
        var block = ms.ToArray();

        var insertAt = parsed.FileEndPushOffset;
        var newData = new byte[data.Length + block.Length];
        Buffer.BlockCopy(data, 0, newData, 0, insertAt);
        Buffer.BlockCopy(block, 0, newData, insertAt, block.Length);
        Buffer.BlockCopy(data, insertAt, newData, insertAt + block.Length, data.Length - insertAt);
        return newData;
    }

    private static byte[] BuildAssignment(DialogEsParsed parsed, DialogEsAssignment add, string latin1Text)
    {
        var memberIdx = add.Space == DialogEsSpace.Question ? parsed.IdxQ : parsed.IdxA;
        using var ms = new MemoryStream();
        ms.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(parsed.IdxD) }));
        ms.WriteByte(Avm1Opcode.GetVariable);
        ms.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(memberIdx) }));
        ms.WriteByte(Avm1Opcode.GetMember);
        ms.Write(Avm1Bytecode.EncodePush(new[]
        {
            Avm1PushItem.Integer(add.Id),
            Avm1PushItem.StringLiteral(latin1Text),
        }));
        ms.WriteByte(Avm1Opcode.SetMember);
        return ms.ToArray();
    }
}
