using System.Globalization;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// CONT.6B — local dialog_es N+1 generation. Never writes SFTP, versions_es, or production BD.
/// </summary>
public static class DialogEsService
{
    public static DialogEsGenerateResult Generate(DialogEsGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceSwfBytes);
        ArgumentNullException.ThrowIfNull(request.Additions);

        string? outputPath = null;
        try
        {
            foreach (var add in request.Additions)
                DialogEsLatin1.Validate(add.Text, add.Space == DialogEsSpace.Question ? "D.q" : "D.a");

            var parsed = DialogEsParser.ParseInternal(request.SourceSwfBytes);
            foreach (var add in request.Additions)
            {
                if (add.Space == DialogEsSpace.Question && parsed.Questions.ContainsKey(add.Id))
                    return Fail($"No se puede modificar D.q[{add.Id}] existente.");
                if (add.Space == DialogEsSpace.Answer && parsed.Answers.ContainsKey(add.Id))
                    return Fail($"No se puede modificar D.a[{add.Id}] existente.");
            }

            var sourceSnap = DialogEsParser.ToSnapshot(parsed);
            var targetVersion = parsed.VersionValue + 1;
            var newAction = DialogEsMutator.Apply(parsed, targetVersion, request.Additions);
            var outBytes = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, newAction).Write(compress: true);

            if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                Directory.CreateDirectory(request.OutputDirectory);
                outputPath = Path.Combine(
                    request.OutputDirectory,
                    string.Create(CultureInfo.InvariantCulture, $"dialog_es_{targetVersion}.swf"));
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

            RufusLog.Ok($"dialog_es_{targetVersion}.swf validado (local)");
            return new DialogEsGenerateResult
            {
                Success = true,
                OutputBytes = outBytes,
                OutputPath = outputPath,
                SourceVersion = parsed.VersionValue,
                TargetVersion = targetVersion,
                SourceSnapshot = sourceSnap,
                OutputSnapshot = DialogEsParser.Parse(outBytes),
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
        IReadOnlyList<DialogEsAssignment> additions)
    {
        if (outputBytes.Length < 4 || outputBytes[0] != (byte)'C' || outputBytes[1] != (byte)'W' || outputBytes[2] != (byte)'S')
            return "Validación: firma CWS inválida.";
        if (outputBytes[3] != 6)
            return $"Validación: SWF v{outputBytes[3]}, esperado 6.";

        DialogEsSnapshot source;
        DialogEsSnapshot output;
        try
        {
            source = DialogEsParser.Parse(sourceBytes);
            output = DialogEsParser.Parse(outputBytes);
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
            return "Validación: ConstantPool modificado (no permitido en CONT.6B).";
        var addedQ = additions.Count(a => a.Space == DialogEsSpace.Question);
        var addedA = additions.Count(a => a.Space == DialogEsSpace.Answer);
        if (output.QuestionAssignmentCount != source.QuestionAssignmentCount + addedQ)
            return "Validación: recuento D.q inesperado (¿corrupción DoAction?).";
        if (output.AnswerAssignmentCount != source.AnswerAssignmentCount + addedA)
            return "Validación: recuento D.a inesperado (¿corrupción DoAction?).";

        foreach (var (id, text) in source.Questions)
        {
            if (!output.Questions.TryGetValue(id, out var now) || now != text)
                return $"Validación: D.q[{id}] alterado.";
        }

        foreach (var (id, text) in source.Answers)
        {
            if (!output.Answers.TryGetValue(id, out var now) || now != text)
                return $"Validación: D.a[{id}] alterado.";
        }

        var extraQ = output.Questions.Keys.Except(source.Questions.Keys).OrderBy(x => x).ToList();
        var extraA = output.Answers.Keys.Except(source.Answers.Keys).OrderBy(x => x).ToList();
        var expectedQ = additions.Where(a => a.Space == DialogEsSpace.Question).Select(a => a.Id).Distinct().OrderBy(x => x).ToList();
        var expectedA = additions.Where(a => a.Space == DialogEsSpace.Answer).Select(a => a.Id).Distinct().OrderBy(x => x).ToList();
        if (!extraQ.SequenceEqual(expectedQ))
            return "Validación: conjunto D.q nuevos no coincide.";
        if (!extraA.SequenceEqual(expectedA))
            return "Validación: conjunto D.a nuevos no coincide.";

        var qAdds = additions.Where(a => a.Space == DialogEsSpace.Question).GroupBy(a => a.Id).ToList();
        var outputParsed = DialogEsParser.ParseInternal(outputBytes);
        foreach (var g in qAdds)
        {
            var last = g.Last().Text;
            if (!output.Questions.TryGetValue(g.Key, out var t) || t != last)
                return $"Validación: D.q[{g.Key}] texto incorrecto.";
            var count = outputParsed.QuestionHits.GetValueOrDefault(g.Key);
            if (count != g.Count())
                return $"Validación: D.q[{g.Key}] aparece {count} veces, esperado {g.Count()}.";
        }

        var aAdds = additions.Where(a => a.Space == DialogEsSpace.Answer).GroupBy(a => a.Id).ToList();
        foreach (var g in aAdds)
        {
            var last = g.Last().Text;
            if (!output.Answers.TryGetValue(g.Key, out var t) || t != last)
                return $"Validación: D.a[{g.Key}] texto incorrecto.";
            var count = outputParsed.AnswerHits.GetValueOrDefault(g.Key);
            if (count != g.Count())
                return $"Validación: D.a[{g.Key}] aparece {count} veces, esperado {g.Count()}.";
        }

        return null;
    }

    private static DialogEsGenerateResult Fail(string error) => new()
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
