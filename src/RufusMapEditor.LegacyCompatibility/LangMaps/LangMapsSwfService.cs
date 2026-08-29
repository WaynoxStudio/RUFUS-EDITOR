using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>
/// FASE 11A — generador local/offline de maps_es CWS. No toca BD, SFTP ni versions_es.txt.
/// </summary>
public static class LangMapsSwfService
{
    public const string EpUndefinedMessage = "EP no definido para publicación LANG.";

    public static LangMapsInspectResult Inspect(string swfPath)
    {
        var bytes = File.ReadAllBytes(swfPath);
        var parsed = LangMapsParser.Parse(bytes);
        RufusLog.Info($"SWF origen: {Path.GetFileName(swfPath)}");
        RufusLog.Info($"VERSION detectada: {parsed.VersionValue}");
        return new LangMapsInspectResult
        {
            SourcePath = swfPath,
            Version = parsed.VersionValue,
            EntryCount = parsed.Entries.Count,
            Entries = LangMapsParser.ToPublicEntries(parsed),
            WasCompressed = parsed.Container.WasCompressed,
            SwfVersion = parsed.Container.Version,
        };
    }

    public static LangMapsGenerateResult Generate(LangMapsGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Ep is null)
            return Fail(EpUndefinedMessage);

        return GenerateBatch(new LangMapsBatchGenerateRequest
        {
            SourceSwfPath = request.SourceSwfPath,
            OutputDirectory = request.OutputDirectory,
            Entries =
            [
                new LangMapsBatchEntry
                {
                    MapId = request.MapId,
                    X = request.X,
                    Y = request.Y,
                    SubArea = request.SubArea,
                    Ep = request.Ep.Value,
                },
            ],
        });
    }

    /// <summary>
    /// MAP-BATCH.1 — apply all MA.m entries then bump VERSION once (N → N+1).
    /// Reuses <see cref="LangMapsMutator.Apply"/> without changing mutator internals.
    /// </summary>
    public static LangMapsGenerateResult GenerateBatch(LangMapsBatchGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourceSwfPath) || !File.Exists(request.SourceSwfPath))
            return Fail("Archivo maps_es de origen no encontrado.");
        if (request.Entries is null || request.Entries.Count == 0)
            return Fail("Lote vacío: no hay mapas para maps_es.");

        var seen = new HashSet<int>();
        foreach (var e in request.Entries)
        {
            if (e.MapId <= 0)
                return Fail($"MapId inválido en lote: {e.MapId}.");
            if (!seen.Add(e.MapId))
                return Fail($"MapId duplicado en lote maps_es: {e.MapId}.");
        }

        string? outputPath = null;
        try
        {
            RufusLog.Info($"SWF origen: {Path.GetFileName(request.SourceSwfPath)}");
            var originalBytes = File.ReadAllBytes(request.SourceSwfPath);
            var originalSha = System.Security.Cryptography.SHA256.HashData(originalBytes);
            var firstParse = LangMapsParser.Parse(originalBytes);
            var sourceVersion = firstParse.VersionValue;
            var targetVersion = sourceVersion + 1;
            RufusLog.Info(
                $"VERSION detectada: {sourceVersion} → generación {targetVersion} · lote {request.Entries.Count} mapas");

            Directory.CreateDirectory(request.OutputDirectory);
            outputPath = Path.Combine(request.OutputDirectory, $"maps_es_{targetVersion}.swf");
            if (string.Equals(
                    Path.GetFullPath(outputPath),
                    Path.GetFullPath(request.SourceSwfPath),
                    StringComparison.OrdinalIgnoreCase))
                return Fail("El destino no puede sobrescribir el SWF de origen.");

            var working = originalBytes;
            var entryResults = new List<(int MapId, bool Inserted, bool Updated)>();
            var anyInsert = false;
            var anyUpdate = false;

            foreach (var entry in request.Entries)
            {
                RufusLog.Info($"Lote maps_es · MapId={entry.MapId} → VERSION fija {targetVersion}");
                var parsed = LangMapsParser.Parse(working);
                var newAction = LangMapsMutator.Apply(
                    parsed,
                    entry.MapId,
                    entry.X,
                    entry.Y,
                    entry.SubArea,
                    entry.Ep,
                    targetVersion,
                    out var inserted,
                    out var updated);
                working = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, newAction).Write(compress: true);
                entryResults.Add((entry.MapId, inserted, updated));
                anyInsert |= inserted;
                anyUpdate |= updated;
            }

            File.WriteAllBytes(outputPath, working);

            var originalNow = File.ReadAllBytes(request.SourceSwfPath);
            if (!System.Security.Cryptography.SHA256.HashData(originalNow).SequenceEqual(originalSha))
            {
                TryDelete(outputPath);
                RufusLog.Error("El SWF original fue modificado; operación abortada");
                return Fail("El SWF original fue modificado; operación abortada.");
            }

            foreach (var entry in request.Entries)
            {
                var validationError = ValidateGenerated(
                    outputPath, targetVersion, entry.MapId, entry.X, entry.Y, entry.SubArea, entry.Ep);
                if (validationError is not null)
                {
                    TryDelete(outputPath);
                    RufusLog.Error(validationError);
                    return Fail(validationError);
                }
            }

            RufusLog.Ok($"Validación OK · maps_es_{targetVersion}.swf · {request.Entries.Count} mapas");
            return new LangMapsGenerateResult
            {
                Success = true,
                OutputPath = outputPath,
                SourceVersion = sourceVersion,
                TargetVersion = targetVersion,
                Inserted = anyInsert,
                Updated = anyUpdate,
                EntryResults = entryResults,
            };
        }
        catch (Exception ex)
        {
            if (outputPath is not null)
                TryDelete(outputPath);
            RufusLog.Error("Generación LANG batch: " + ex.Message);
            return Fail(ex.Message);
        }
    }

    public static string? ValidateGenerated(
        string path, int expectedVersion, int mapId, int x, int y, int sa, int ep)
    {
        try
        {
            var parsed = LangMapsParser.Parse(File.ReadAllBytes(path));
            if (parsed.VersionValue != expectedVersion)
                return $"Validación: VERSION={parsed.VersionValue}, esperado {expectedVersion}.";

            var matches = parsed.Entries.Where(e => e.MapId == mapId).ToList();
            if (matches.Count == 0)
                return $"Validación: MA.m[{mapId}] no encontrada.";
            if (matches.Count != 1)
                return $"Validación: MA.m[{mapId}] aparece {matches.Count} veces (debe ser 1).";

            var e = matches[0];
            if (e.IntProps.GetValueOrDefault("x") != x)
                return $"Validación: X incorrecta ({e.IntProps.GetValueOrDefault("x")} ≠ {x}).";
            if (e.IntProps.GetValueOrDefault("y") != y)
                return $"Validación: Y incorrecta ({e.IntProps.GetValueOrDefault("y")} ≠ {y}).";
            if (e.IntProps.GetValueOrDefault("sa") != sa)
                return $"Validación: SA incorrecta ({e.IntProps.GetValueOrDefault("sa")} ≠ {sa}).";
            if (e.IntProps.GetValueOrDefault("ep") != ep)
                return $"Validación: EP incorrecta ({e.IntProps.GetValueOrDefault("ep")} ≠ {ep}).";

            var hasFileEnd = parsed.Actions.Any(a =>
                a.Code == Avm1Opcode.Push
                && a.PushItems is not null
                && a.PushItems.Any(p =>
                    (p.Type == Avm1PushType.String && p.StringValue == "FILE_END")
                    || Avm1Bytecode.IsConstantNamed(p, "FILE_END")));
            if (!hasFileEnd)
                return "Validación: FILE_END ausente.";

            _ = SwfContainer.Read(File.ReadAllBytes(path));
            return null;
        }
        catch (Exception ex)
        {
            return "Validación: " + ex.Message;
        }
    }

    private static LangMapsGenerateResult Fail(string error) => new() { Success = false, Error = error };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }
}
