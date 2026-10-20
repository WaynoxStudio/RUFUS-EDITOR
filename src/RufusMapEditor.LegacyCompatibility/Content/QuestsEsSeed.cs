using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Minimal CWS quests_es for local tests (Q.q/s/o/t / VERSION / FILE_END).</summary>
public static class QuestsEsSeed
{
    public static byte[] Create(int version, IEnumerable<QuestsEsMissionAssignment>? seed = null)
    {
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        var additions = seed?.ToList() ?? new List<QuestsEsMissionAssignment>();
        if (additions.Count == 0)
            return BuildEmpty(version);

        if (version <= 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Seed con misiones requiere version > 1.");

        var inner = BuildEmpty(version - 1);
        var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
        {
            SourceSwfBytes = inner,
            Additions = additions,
        });
        if (!gen.Success || gen.OutputBytes is null)
            throw new InvalidOperationException(gen.Error ?? "QuestsEsSeed falló.");
        return gen.OutputBytes;
    }

    private static byte[] BuildEmpty(int version)
    {
        var pool = new List<string>
        {
            "VERSION", "Q", "q", "s", "o", "t", "n", "d", "r", "rbl", "p", "x", "y", "FILE_END",
        };
        using var body = new MemoryStream();
        WriteConstantPool(body, pool);

        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(0) }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(version) }));
        body.WriteByte(Avm1Opcode.SetVariable);

        foreach (var idx in new[] { 2, 3, 4, 5 })
        {
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
            body.WriteByte(Avm1Opcode.GetVariable);
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(idx) }));
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(0) }));
            body.WriteByte(Avm1Opcode.InitObject);
            body.WriteByte(Avm1Opcode.SetMember);
        }

        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
        body.WriteByte(Avm1Opcode.GetVariable);
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(5) }));
        body.WriteByte(Avm1Opcode.GetMember);
        body.Write(Avm1Bytecode.EncodePush(new[]
        {
            Avm1PushItem.Integer(1),
            Avm1PushItem.StringLiteral("Objetivo"),
        }));
        body.WriteByte(Avm1Opcode.SetMember);

        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.StringLiteral("FILE_END") }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Boolean(true) }));
        body.WriteByte(Avm1Opcode.SetVariable);
        body.WriteByte(Avm1Opcode.End);

        return new SwfContainer
        {
            Version = 6,
            FrameRateFixed = 12 << 8,
            FrameCount = 1,
            FrameSizeRectBytes = new byte[] { 0x00 },
            Tags = new[]
            {
                new SwfTag { Code = 9, Data = new byte[] { 0x00, 0x00, 0x00 } },
                new SwfTag { Code = 12, Data = body.ToArray() },
                new SwfTag { Code = 1, Data = Array.Empty<byte>() },
                new SwfTag { Code = 0, Data = Array.Empty<byte>() },
            },
            WasCompressed = true,
        }.Write(compress: true);
    }

    private static void WriteConstantPool(Stream stream, IReadOnlyList<string> pool)
    {
        using var payload = new MemoryStream();
        payload.Write(BitConverter.GetBytes((ushort)pool.Count));
        foreach (var s in pool)
        {
            var bytes = Avm1Bytecode.SwfStringEncoding.GetBytes(s);
            payload.Write(bytes);
            payload.WriteByte(0);
        }

        var p = payload.ToArray();
        stream.WriteByte(Avm1Opcode.ConstantPool);
        stream.Write(BitConverter.GetBytes((ushort)p.Length));
        stream.Write(p);
    }
}
