using System.Globalization;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

public static class QuestsEsService
{
    public static QuestsEsGenerateResult Generate(QuestsEsGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceSwfBytes);
        ArgumentNullException.ThrowIfNull(request.Additions);

        string? outputPath = null;
        try
        {
            foreach (var mission in request.Additions)
            {
                if (mission.WriteQuest)
                    DialogEsLatin1.Prepare(mission.Quest.Name, $"Q.q[{mission.Quest.QuestId}]");
                foreach (var s in mission.Stages)
                {
                    DialogEsLatin1.Prepare(s.Name, $"Q.s[{s.StageId}].n");
                    _ = QuestsEsCodec.EncodeStageDescription(s.Description);
                }
            }

            var parsed = QuestsEsParser.ParseInternal(request.SourceSwfBytes);
            var sourceSnap = QuestsEsParser.ToSnapshot(parsed);
            var targetVersion = parsed.VersionValue + 1;
            var newAction = QuestsEsMutator.Apply(parsed, targetVersion, request.Additions);
            var outBytes = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, newAction).Write(compress: true);

            if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                Directory.CreateDirectory(request.OutputDirectory);
                outputPath = Path.Combine(
                    request.OutputDirectory,
                    string.Create(CultureInfo.InvariantCulture, $"quests_es_{targetVersion}.swf"));
                if (File.Exists(outputPath))
                    return Fail($"El destino ya existe: {outputPath}");
                File.WriteAllBytes(outputPath, outBytes);
            }

            var error = ValidateGenerated(request.SourceSwfBytes, outBytes, request.Additions, sourceSnap);
            if (error is not null)
            {
                if (outputPath is not null)
                    TryDelete(outputPath);
                return Fail(error);
            }

            RufusLog.Ok($"quests_es_{targetVersion}.swf validado (local)");
            return new QuestsEsGenerateResult
            {
                Success = true,
                OutputBytes = outBytes,
                OutputPath = outputPath,
                SourceVersion = parsed.VersionValue,
                TargetVersion = targetVersion,
                SourceSnapshot = sourceSnap,
                OutputSnapshot = QuestsEsParser.Parse(outBytes),
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
        IReadOnlyList<QuestsEsMissionAssignment> additions,
        QuestsEsSnapshot? sourceSnapshot = null)
    {
        if (outputBytes.Length < 4 || outputBytes[0] != (byte)'C' || outputBytes[1] != (byte)'W' || outputBytes[2] != (byte)'S')
            return "Validación: firma CWS inválida.";
        if (outputBytes[3] != 6)
            return $"Validación: SWF v{outputBytes[3]}, esperado 6.";

        QuestsEsSnapshot source;
        QuestsEsSnapshot output;
        try
        {
            source = sourceSnapshot ?? QuestsEsParser.Parse(sourceBytes);
            output = QuestsEsParser.Parse(outputBytes);
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

        var newQuests = additions.Count(a => a.WriteQuest);
        var newStages = additions.Sum(a => a.Stages.Count);
        var newObjectives = additions.Sum(a => a.Objectives.Count);
        if (output.QuestAssignmentCount != source.QuestAssignmentCount + newQuests)
            return "Validación: recuento Q.q inesperado.";
        if (output.StageAssignmentCount != source.StageAssignmentCount + newStages)
            return "Validación: recuento Q.s inesperado.";
        if (output.ObjectiveAssignmentCount != source.ObjectiveAssignmentCount + newObjectives)
            return "Validación: recuento Q.o inesperado.";

        var replacedQuests = additions.Where(a => a.WriteQuest).Select(a => a.Quest.QuestId).ToHashSet();
        var replacedStages = additions.SelectMany(a => a.Stages.Select(s => s.StageId)).ToHashSet();
        var replacedObjectives = additions.SelectMany(a => a.Objectives.Select(o => o.ObjectiveId)).ToHashSet();

        foreach (var (id, entry) in source.Quests)
        {
            if (replacedQuests.Contains(id))
                continue;
            if (!output.Quests.TryGetValue(id, out var now)
                || now.Name != entry.Name
                || !now.StageIds.SequenceEqual(entry.StageIds))
                return $"Validación: Q.q[{id}] alterado.";
        }

        foreach (var (id, entry) in source.Stages)
        {
            if (replacedStages.Contains(id))
                continue;
            if (!output.Stages.TryGetValue(id, out var now)
                || now.Name != entry.Name
                || !SameDescription(entry.Description, now.Description)
                || !SameRewardList(entry.Rewards, now.Rewards))
                return $"Validación: Q.s[{id}] alterado.";
        }

        foreach (var (id, entry) in source.Objectives)
        {
            if (replacedObjectives.Contains(id))
                continue;
            if (!output.Objectives.TryGetValue(id, out var now)
                || now.Type != entry.Type
                || now.X != entry.X
                || now.Y != entry.Y
                || !SameObjectList(entry.Parameters, now.Parameters))
                return $"Validación: Q.o[{id}] alterado.";
        }

        foreach (var (id, text) in source.TypeTemplates)
        {
            if (!output.TypeTemplates.TryGetValue(id, out var now) || now != text)
                return $"Validación: Q.t[{id}] alterado.";
        }

        if (output.TypeTemplates.Count != source.TypeTemplates.Count)
            return "Validación: conjunto Q.t modificado.";

        foreach (var mission in additions)
        {
            if (mission.WriteQuest)
            {
                var q = mission.Quest;
                if (!output.Quests.TryGetValue(q.QuestId, out var qOut)
                    || qOut.Name != DialogEsLatin1.Normalize(q.Name)
                    || !qOut.IsStringName)
                    return $"Validación: Q.q[{q.QuestId}] incorrecto.";
            }

            foreach (var s in mission.Stages)
            {
                if (!output.Stages.TryGetValue(s.StageId, out var sOut)
                    || sOut.Name != DialogEsLatin1.Normalize(s.Name))
                    return $"Validación: Q.s[{s.StageId}].n incorrecto.";
                var expectedR = QuestsEsCodec.EncodeRewards(s.Rewards);
                if (!SameRewardList(expectedR, sOut.Rewards))
                    return $"Validación: Q.s[{s.StageId}].r incorrecto.";
            }

            foreach (var o in mission.Objectives)
            {
                if (!output.Objectives.TryGetValue(o.ObjectiveId, out var oOut)
                    || oOut.Type != o.Type)
                    return $"Validación: Q.o[{o.ObjectiveId}] incorrecto.";

                var draft = new MissionObjectiveDraft
                {
                    Id = o.ObjectiveId,
                    Tipo = o.Type,
                    Args = o.Args,
                    Detalle = o.Detalle,
                };
                var expectedP = QuestsEsCodec.EncodeObjectiveParameters(draft);
                if (!SameObjectList(expectedP, oOut.Parameters))
                    return $"Validación: Q.o[{o.ObjectiveId}].p incorrecto.";
            }
        }

        return null;
    }

    private static bool SameDescription(object a, object b) =>
        (a is int ai && b is int bi && ai == bi)
        || (a is string sa && b is string sb && sa == sb)
        || Equals(a, b);

    private static bool SameRewardList(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!SameValue(a[i], b[i]))
                return false;
        }

        return true;
    }

    private static bool SameObjectList(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!SameValue(a[i], b[i]))
                return false;
        }

        return true;
    }

    private static bool SameValue(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is int ai && b is int bi) return ai == bi;
        if (a is string sa && b is string sb) return sa == sb;
        if (a is int[] ia && b is int[] ib)
            return ia.AsSpan().SequenceEqual(ib);
        if (TryAsObjectSequence(a, out var la) && TryAsObjectSequence(b, out var lb))
        {
            if (la.Count != lb.Count) return false;
            for (var i = 0; i < la.Count; i++)
            {
                if (!SameValue(la[i], lb[i]))
                    return false;
            }

            return true;
        }

        return Equals(a, b);
    }

    private static bool TryAsObjectSequence(object value, out IReadOnlyList<object?> list)
    {
        if (value is int[] ints)
        {
            list = ints.Cast<object?>().ToList();
            return true;
        }

        if (value is object[] arr)
        {
            list = arr;
            return true;
        }

        if (value is IReadOnlyList<object?> ro)
        {
            list = ro;
            return true;
        }

        if (value is System.Collections.IList il && value is not string)
        {
            var tmp = new List<object?>(il.Count);
            foreach (var item in il)
                tmp.Add(item);
            list = tmp;
            return true;
        }

        list = Array.Empty<object?>();
        return false;
    }

    private static QuestsEsGenerateResult Fail(string error) => new()
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
