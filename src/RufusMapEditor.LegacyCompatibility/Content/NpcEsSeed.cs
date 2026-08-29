using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Minimal CWS npc_es for local tests (N.d / N.a / VERSION / FILE_END).</summary>
public static class NpcEsSeed
{
    public static byte[] Create(int version, IEnumerable<NpcEsAssignment>? seed = null)
    {
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        var additions = seed?.ToList() ?? new List<NpcEsAssignment>();
        foreach (var a in additions)
            DialogEsLatin1.Validate(a.Name);

        // pool: 0 VERSION, 1 N, 2 d, 3 a, 4 n
        var pool = new List<string> { "VERSION", "N", "d", "a", "n" };
        using var body = new MemoryStream();
        WriteConstantPool(body, pool);

        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(0) }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(version) }));
        body.WriteByte(Avm1Opcode.SetVariable);

        // N = {}
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(0) }));
        body.WriteByte(Avm1Opcode.InitObject);
        body.WriteByte(Avm1Opcode.SetVariable);

        // N.d = {}
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
        body.WriteByte(Avm1Opcode.GetVariable);
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(2) }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(0) }));
        body.WriteByte(Avm1Opcode.InitObject);
        body.WriteByte(Avm1Opcode.SetMember);

        // N.a = {}
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
        body.WriteByte(Avm1Opcode.GetVariable);
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(3) }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(0) }));
        body.WriteByte(Avm1Opcode.InitObject);
        body.WriteByte(Avm1Opcode.SetMember);

        // sample action label
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
        body.WriteByte(Avm1Opcode.GetVariable);
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(3) }));
        body.WriteByte(Avm1Opcode.GetMember);
        body.Write(Avm1Bytecode.EncodePush(new[]
        {
            Avm1PushItem.Integer(3),
            Avm1PushItem.StringLiteral("Hablar"),
        }));
        body.WriteByte(Avm1Opcode.SetMember);

        foreach (var add in additions)
        {
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
            body.WriteByte(Avm1Opcode.GetVariable);
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(2) }));
            body.WriteByte(Avm1Opcode.GetMember);
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(add.Id) }));
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(4) }));
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.StringLiteral(add.Name) }));
            var acts = NpcEsClientActions.Normalize(add.Actions);
            var propCount = 1;
            if (acts.Count > 0)
            {
                body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(3) })); // a
                for (var i = acts.Count - 1; i >= 0; i--)
                    body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(acts[i]) }));
                body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(acts.Count) }));
                body.WriteByte(Avm1Opcode.InitArray);
                propCount = 2;
            }

            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(propCount) }));
            body.WriteByte(Avm1Opcode.InitObject);
            body.WriteByte(Avm1Opcode.SetMember);
        }

        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.StringLiteral("FILE_END") }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Boolean(true) }));
        body.WriteByte(Avm1Opcode.SetVariable);
        body.WriteByte(Avm1Opcode.End);

        var action = body.ToArray();
        var container = new SwfContainer
        {
            Version = 6,
            FrameRateFixed = 12 << 8,
            FrameCount = 1,
            FrameSizeRectBytes = new byte[] { 0x00 },
            Tags = new[]
            {
                new SwfTag { Code = 9, Data = new byte[] { 0x00, 0x00, 0x00 } },
                new SwfTag { Code = 12, Data = action },
                new SwfTag { Code = 1, Data = Array.Empty<byte>() },
                new SwfTag { Code = 0, Data = Array.Empty<byte>() },
            },
            WasCompressed = true,
        };
        return container.Write(compress: true);
    }

    private static void WriteConstantPool(Stream stream, IReadOnlyList<string> pool)
    {
        using var payload = new MemoryStream();
        payload.Write(BitConverter.GetBytes((ushort)pool.Count));
        foreach (var s in pool)
        {
            var bytes = Encoding.Latin1.GetBytes(s);
            payload.Write(bytes);
            payload.WriteByte(0);
        }

        var p = payload.ToArray();
        stream.WriteByte(Avm1Opcode.ConstantPool);
        stream.Write(BitConverter.GetBytes((ushort)p.Length));
        stream.Write(p);
    }
}
