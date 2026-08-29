using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Builds a minimal CWS dialog_es for local tests (not production content).</summary>
public static class DialogEsSeed
{
    public static byte[] Create(int version, IEnumerable<DialogEsAssignment>? seed = null)
    {
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        var additions = seed?.ToList() ?? new List<DialogEsAssignment>();
        foreach (var a in additions)
            DialogEsLatin1.Validate(a.Text);

        var pool = new List<string> { "VERSION", "D", "q", "a" };
        using var body = new MemoryStream();
        WriteConstantPool(body, pool);
        var versionIntOffsetPlaceholder = (int)body.Length;
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(0) })); // VERSION
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(version) }));
        body.WriteByte(Avm1Opcode.SetVariable);

        // D = {}
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(0) }));
        body.WriteByte(Avm1Opcode.InitObject);
        body.WriteByte(Avm1Opcode.SetVariable);

        // D.q = {}
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
        body.WriteByte(Avm1Opcode.GetVariable);
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(2) }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(0) }));
        body.WriteByte(Avm1Opcode.InitObject);
        body.WriteByte(Avm1Opcode.SetMember);

        // D.a = {}
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
        body.WriteByte(Avm1Opcode.GetVariable);
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(3) }));
        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Integer(0) }));
        body.WriteByte(Avm1Opcode.InitObject);
        body.WriteByte(Avm1Opcode.SetMember);

        foreach (var add in additions)
        {
            var member = add.Space == DialogEsSpace.Question ? 2 : 3;
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(1) }));
            body.WriteByte(Avm1Opcode.GetVariable);
            body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.Constant(member) }));
            body.WriteByte(Avm1Opcode.GetMember);
            body.Write(Avm1Bytecode.EncodePush(new[]
            {
                Avm1PushItem.Integer(add.Id),
                Avm1PushItem.StringLiteral(add.Text),
            }));
            body.WriteByte(Avm1Opcode.SetMember);
        }

        body.Write(Avm1Bytecode.EncodePush(new[] { Avm1PushItem.StringLiteral("FILE_END") }));
        body.WriteByte(Avm1Opcode.SetVariable);
        body.WriteByte(Avm1Opcode.End);

        var action = body.ToArray();
        _ = versionIntOffsetPlaceholder;

        var container = new SwfContainer
        {
            Version = 6,
            FrameRateFixed = 12 << 8,
            FrameCount = 1,
            FrameSizeRectBytes = new byte[] { 0x00 },
            Tags = new[]
            {
                new SwfTag { Code = 9, Data = new byte[] { 0x00, 0x00, 0x00 } }, // SetBackgroundColor
                new SwfTag { Code = 12, Data = action },
                new SwfTag { Code = 1, Data = Array.Empty<byte>() }, // ShowFrame
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
