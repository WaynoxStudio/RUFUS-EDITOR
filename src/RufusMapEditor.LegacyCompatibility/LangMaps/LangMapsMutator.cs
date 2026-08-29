namespace RufusMapEditor.LegacyCompatibility.LangMaps;

internal static class LangMapsMutator
{
    public static byte[] Apply(
        LangMapsParsed parsed,
        int mapId,
        int x,
        int y,
        int subArea,
        int ep,
        int newVersion,
        out bool inserted,
        out bool updated)
    {
        inserted = false;
        updated = false;
        var data = (byte[])parsed.ActionData.Clone();
        BitConverter.TryWriteBytes(data.AsSpan(parsed.VersionIntOffset, 4), newVersion);

        var matches = parsed.Entries.Where(e => e.MapId == mapId).OrderBy(e => e.ActionStart).ToList();
        if (matches.Count > 0)
        {
            PatchEntryInts(data, matches[0], x, y, subArea, ep);
            if (matches.Count > 1)
            {
                foreach (var dup in matches.Skip(1).OrderByDescending(e => e.ActionStart))
                    data = RemoveRange(data, dup.ActionStart, dup.ActionEnd);
            }

            updated = true;
            return data;
        }

        var block = BuildNewEntryBlock(
            parsed.IdxMa, parsed.IdxM, parsed.IdxX, parsed.IdxY, parsed.IdxSa, parsed.IdxEp,
            mapId, x, y, subArea, ep);

        var insertAt = parsed.FileEndPushOffset;
        var newData = new byte[data.Length + block.Length];
        Buffer.BlockCopy(data, 0, newData, 0, insertAt);
        Buffer.BlockCopy(block, 0, newData, insertAt, block.Length);
        Buffer.BlockCopy(data, insertAt, newData, insertAt + block.Length, data.Length - insertAt);
        inserted = true;
        return newData;
    }

    private static void PatchEntryInts(byte[] data, LangMaEntrySpan entry, int x, int y, int sa, int ep)
    {
        void Patch(string key, int value)
        {
            if (!entry.IntValueOffsets.TryGetValue(key, out var off))
                throw new InvalidOperationException(
                    $"MA.m[{entry.MapId}]: no se pudo localizar el entero '{key}' para actualizar.");
            BitConverter.TryWriteBytes(data.AsSpan(off, 4), value);
        }

        foreach (var key in new[] { "x", "y", "sa", "ep" })
        {
            if (!entry.IntProps.ContainsKey(key) || !entry.IntValueOffsets.ContainsKey(key))
                throw new InvalidOperationException(
                    $"MA.m[{entry.MapId}]: falta propiedad '{key}' para actualizar.");
        }

        Patch("x", x);
        Patch("y", y);
        Patch("sa", sa);
        Patch("ep", ep);
    }

    internal static byte[] BuildNewEntryBlock(
        int idxMa, int idxM, int idxX, int idxY, int idxSa, int idxEp,
        int mapId, int x, int y, int sa, int ep)
    {
        using var ms = new MemoryStream();
        void WriteRaw(byte b) => ms.WriteByte(b);
        void WritePush(params Avm1PushItem[] items) => ms.Write(Avm1Bytecode.EncodePush(items));

        WritePush(Avm1PushItem.Constant(idxMa));
        WriteRaw(Avm1Opcode.GetVariable);
        WritePush(Avm1PushItem.Constant(idxM));
        WriteRaw(Avm1Opcode.GetMember);
        WritePush(Avm1PushItem.Integer(mapId), Avm1PushItem.Constant(idxEp));
        WritePush(Avm1PushItem.Integer(ep));
        WritePush(Avm1PushItem.Constant(idxSa));
        WritePush(Avm1PushItem.Integer(sa));
        WritePush(Avm1PushItem.Constant(idxY));
        WritePush(Avm1PushItem.Integer(y));
        WritePush(Avm1PushItem.Constant(idxX));
        WritePush(Avm1PushItem.Integer(x));
        WritePush(Avm1PushItem.Integer(4));
        WriteRaw(Avm1Opcode.InitObject);
        WriteRaw(Avm1Opcode.SetMember);
        return ms.ToArray();
    }

    private static byte[] RemoveRange(byte[] data, int start, int end)
    {
        if (start < 0 || end > data.Length || end < start)
            throw new InvalidOperationException("Rango AVM1 inválido al eliminar duplicado MA.m.");
        var len = end - start;
        var result = new byte[data.Length - len];
        Buffer.BlockCopy(data, 0, result, 0, start);
        Buffer.BlockCopy(data, end, result, start, data.Length - end);
        return result;
    }
}
