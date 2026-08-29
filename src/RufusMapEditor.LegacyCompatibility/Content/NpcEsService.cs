using System.Globalization;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.7B.1 — local npc_es N+1 generation with optional a:[...] and updates via append-overwrite.</summary>
public static class NpcEsService
{
    public static NpcEsGenerateResult Generate(NpcEsGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceSwfBytes);
        ArgumentNullException.ThrowIfNull(request.Additions);

        string? outputPath = null;
        try
        {
            foreach (var add in request.Additions)
            {
                DialogEsLatin1.Validate(add.Name, $"N.d[{add.Id}].n");
                foreach (var a in add.Actions)
                {
                    if (!NpcEsClientActions.IsValid(a))
                        return Fail($"Acción npc_es inválida {a} en NPC {add.Id}.");
                }
            }

            var parsed = NpcEsParser.ParseInternal(request.SourceSwfBytes);
            foreach (var add in request.Additions)
            {
                if (!parsed.Names.TryGetValue(add.Id, out var existing))
                    continue;
                // Allow update (same or different actions / rename explicit in batch).
                // Refuse only if somehow empty name.
                if (string.IsNullOrWhiteSpace(add.Name))
                    return Fail($"N.d[{add.Id}]: nombre vacío.");
                _ = existing;
            }

            var sourceSnap = NpcEsParser.ToSnapshot(parsed);
            var targetVersion = parsed.VersionValue + 1;
            var newAction = NpcEsMutator.Apply(parsed, targetVersion, request.Additions);
            var outBytes = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, newAction).Write(compress: true);

            if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                Directory.CreateDirectory(request.OutputDirectory);
                outputPath = Path.Combine(
                    request.OutputDirectory,
                    string.Create(CultureInfo.InvariantCulture, $"npc_es_{targetVersion}.swf"));
                if (File.Exists(outputPath))
                    return Fail($"El destino ya existe: {outputPath}");
                File.WriteAllBytes(outputPath, outBytes);
            }

            var error = ValidateGenerated(request.SourceSwfBytes, outBytes, request.Additions);
            if (error is not null)
            {
                if (outputPath is not null)
                    TryDelete(outputPath);
                return Fail(error);
            }

            RufusLog.Ok($"npc_es_{targetVersion}.swf validado (local)");
            return new NpcEsGenerateResult
            {
                Success = true,
                OutputBytes = outBytes,
                OutputPath = outputPath,
                SourceVersion = parsed.VersionValue,
                TargetVersion = targetVersion,
                SourceSnapshot = sourceSnap,
                OutputSnapshot = NpcEsParser.Parse(outBytes),
            };
        }
        catch (DialogEsEncodingException ex)
        {
            if (outputPath is not null) TryDelete(outputPath);
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            if (outputPath is not null) TryDelete(outputPath);
            return Fail(ex.Message);
        }
    }

    public static string? ValidateGenerated(
        byte[] sourceBytes,
        byte[] outputBytes,
        IReadOnlyList<NpcEsAssignment> additions)
    {
        if (outputBytes.Length < 4 || outputBytes[0] != (byte)'C' || outputBytes[1] != (byte)'W' || outputBytes[2] != (byte)'S')
            return "Validación: firma CWS inválida.";
        if (outputBytes[3] != 6)
            return $"Validación: SWF v{outputBytes[3]}, esperado 6.";

        NpcEsSnapshot source;
        NpcEsSnapshot output;
        try
        {
            source = NpcEsParser.Parse(sourceBytes);
            output = NpcEsParser.Parse(outputBytes);
        }
        catch (Exception ex)
        {
            return "Validación: no reparseable — " + ex.Message;
        }

        if (!output.WasCompressed)
            return "Validación: el SWF generado no está comprimido (CWS).";
        if (output.SwfVersion != 6)
            return $"Validación: SWF v{output.SwfVersion}, esperado 6.";
        if (output.Version != source.Version + 1)
            return $"Validación: VERSION={output.Version}, esperado {source.Version + 1}.";
        if (!output.HasFileEnd)
            return "Validación: FILE_END ausente.";
        if (output.DoActionCount != 1)
            return "Validación: DoAction distinto de 1.";
        if (output.ConstantPoolCount != source.ConstantPoolCount)
            return "Validación: ConstantPool modificado (no permitido).";
        if (output.NameAssignmentCount != source.NameAssignmentCount + additions.Count)
            return "Validación: recuento N.d inesperado.";

        var touched = additions.Select(a => a.Id).ToHashSet();

        foreach (var (id, text) in source.Names)
        {
            if (touched.Contains(id)) continue;
            if (!output.Names.TryGetValue(id, out var now) || now != text)
                return $"Validación: N.d[{id}].n alterado.";
            var srcA = source.ActionsOf(id);
            var outA = output.ActionsOf(id);
            if (!NpcEsClientActions.SameSet(srcA, outA))
                return $"Validación: N.d[{id}].a alterado.";
        }

        foreach (var (id, text) in source.ActionLabels)
        {
            if (!output.ActionLabels.TryGetValue(id, out var now) || now != text)
                return $"Validación: N.a[{id}] alterado.";
        }

        if (output.ActionLabels.Count != source.ActionLabels.Count)
            return "Validación: conjunto N.a modificado.";

        foreach (var g in additions.GroupBy(a => a.Id))
        {
            var last = g.Last();
            if (!output.Names.TryGetValue(last.Id, out var t) || t != last.Name)
                return $"Validación: N.d[{last.Id}].n texto incorrecto.";
            if (!NpcEsClientActions.SameSet(output.ActionsOf(last.Id), last.Actions))
                return $"Validación: N.d[{last.Id}].a incorrecto.";
        }

        // New ids only (updates already existed)
        var newIds = additions.Select(a => a.Id).Where(id => !source.Contains(id)).Distinct().OrderBy(x => x).ToList();
        var extra = output.Names.Keys.Except(source.Names.Keys).OrderBy(x => x).ToList();
        if (!extra.SequenceEqual(newIds))
            return "Validación: conjunto N.d nuevos no coincide.";

        return null;
    }

    private static NpcEsGenerateResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        SourceVersion = 0,
        TargetVersion = 0,
    };

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }
}
