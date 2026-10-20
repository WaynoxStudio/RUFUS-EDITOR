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
    /// Same MapId may appear multiple times when (sa,ep) differ and (x,y) match (dual-layer).
    /// </summary>
    public static LangMapsGenerateResult GenerateBatch(LangMapsBatchGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourceSwfPath) || !File.Exists(request.SourceSwfPath))
            return Fail("Archivo maps_es de origen no encontrado.");
        if (request.Entries is null || request.Entries.Count == 0)
            return Fail("Lote vacío: no hay mapas para maps_es.");

        var groups = new Dictionary<int, List<LangMapsBatchEntry>>();
        foreach (var e in request.Entries)
        {
            if (e.MapId <= 0)
                return Fail($"MapId inválido en lote: {e.MapId}.");
            if (!groups.TryGetValue(e.MapId, out var list))
            {
                list = [];
                groups[e.MapId] = list;
            }

            if (list.Count > 0 && (list[0].X != e.X || list[0].Y != e.Y))
            {
                return Fail(
                    $"MapId {e.MapId}: coords divergentes en lote " +
                    $"({list[0].X},{list[0].Y}) vs ({e.X},{e.Y}).");
            }

            if (list.Any(x => x.SubArea == e.SubArea && x.Ep == e.Ep))
                return Fail($"MapId {e.MapId}: variante sa={e.SubArea} ep={e.Ep} duplicada en lote.");

            list.Add(e);
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
                $"VERSION detectada: {sourceVersion} → generación {targetVersion} · lote {request.Entries.Count} filas / {groups.Count} MapId");

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

            var removeIds = (request.RemoveMapIds ?? Array.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (removeIds.Count > 0)
            {
                var parsedForRemove = LangMapsParser.Parse(working);
                var present = removeIds
                    .Where(id => parsedForRemove.Entries.Any(e => e.MapId == id))
                    .ToList();
                if (present.Count > 0)
                {
                    RufusLog.Info(
                        $"Lote maps_es · quitar MapId [{string.Join(",", present)}] → VERSION fija {targetVersion}");
                    var removedAction = LangMapsMutator.RemoveMapIds(
                        parsedForRemove, present, targetVersion, out _);
                    working = parsedForRemove.Container
                        .WithReplacedTag(parsedForRemove.DoActionTagIndex, removedAction)
                        .Write(compress: true);
                    anyUpdate = true;
                }
            }

            foreach (var (mapId, variants) in groups.OrderBy(g => g.Key))
            {
                RufusLog.Info(
                    $"Lote maps_es · MapId={mapId} · {variants.Count} variante(s) → VERSION fija {targetVersion}");
                var parsed = LangMapsParser.Parse(working);
                byte[] newAction;
                bool inserted;
                bool updated;
                if (variants.Count == 1)
                {
                    var entry = variants[0];
                    newAction = LangMapsMutator.Apply(
                        parsed,
                        entry.MapId,
                        entry.X,
                        entry.Y,
                        entry.SubArea,
                        entry.Ep,
                        targetVersion,
                        out inserted,
                        out updated);
                }
                else
                {
                    newAction = LangMapsMutator.ReplaceVariants(
                        parsed,
                        mapId,
                        variants[0].X,
                        variants[0].Y,
                        variants.Select(v => (v.SubArea, v.Ep)).ToList(),
                        targetVersion,
                        out inserted,
                        out updated);
                }

                working = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, newAction).Write(compress: true);
                entryResults.Add((mapId, inserted, updated));
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

            foreach (var id in removeIds)
            {
                var stillThere = LangMapsParser.Parse(File.ReadAllBytes(outputPath))
                    .Entries.Any(e => e.MapId == id);
                if (stillThere)
                {
                    TryDelete(outputPath);
                    return Fail($"Validación: MA.m[{id}] sigue presente tras eliminar.");
                }
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

            RufusLog.Ok($"Validación OK · maps_es_{targetVersion}.swf · {groups.Count} MapId");
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

    /// <summary>
    /// Removes MA.m entries by MapId and writes maps_es_{target}.swf with bumped VERSION.
    /// Target defaults to filename maps_es_N → N+1 when present; otherwise VersionValue+1.
    /// </summary>
    public static LangMapsGenerateResult RemoveMapIds(
        string sourceSwfPath,
        string outputDirectory,
        IReadOnlyList<int> mapIds,
        int? targetVersionOverride = null)
    {
        if (string.IsNullOrWhiteSpace(sourceSwfPath) || !File.Exists(sourceSwfPath))
            return Fail("Archivo maps_es de origen no encontrado.");
        if (mapIds is null || mapIds.Count == 0)
            return Fail("Lista de MapId vacía.");

        string? outputPath = null;
        try
        {
            var originalBytes = File.ReadAllBytes(sourceSwfPath);
            var originalSha = System.Security.Cryptography.SHA256.HashData(originalBytes);
            var parsed = LangMapsParser.Parse(originalBytes);
            var sourceVersion = parsed.VersionValue;
            var fileVersion = TryParseMapsEsFileVersion(sourceSwfPath);
            var targetVersion = targetVersionOverride
                ?? (fileVersion is int fv ? fv + 1 : sourceVersion + 1);
            RufusLog.Info(
                $"SWF origen: {Path.GetFileName(sourceSwfPath)} · VERSION {sourceVersion}" +
                (fileVersion is int fvv ? $" (archivo {fvv})" : "") +
                $" → {targetVersion} · quitar {mapIds.Count} MapId");

            Directory.CreateDirectory(outputDirectory);
            outputPath = Path.Combine(outputDirectory, $"maps_es_{targetVersion}.swf");
            if (string.Equals(
                    Path.GetFullPath(outputPath),
                    Path.GetFullPath(sourceSwfPath),
                    StringComparison.OrdinalIgnoreCase))
                return Fail("El destino no puede sobrescribir el SWF de origen.");

            var newAction = LangMapsMutator.RemoveMapIds(parsed, mapIds, targetVersion, out var removed);
            var working = parsed.Container.WithReplacedTag(parsed.DoActionTagIndex, newAction).Write(compress: true);
            File.WriteAllBytes(outputPath, working);

            var originalNow = File.ReadAllBytes(sourceSwfPath);
            if (!System.Security.Cryptography.SHA256.HashData(originalNow).SequenceEqual(originalSha))
            {
                TryDelete(outputPath);
                return Fail("El SWF original fue modificado; operación abortada.");
            }

            var check = LangMapsParser.Parse(File.ReadAllBytes(outputPath));
            if (check.VersionValue != targetVersion)
            {
                TryDelete(outputPath);
                return Fail($"Validación: VERSION={check.VersionValue}, esperado {targetVersion}.");
            }

            foreach (var id in mapIds.Where(x => x > 0).Distinct())
            {
                if (check.Entries.Any(e => e.MapId == id))
                {
                    TryDelete(outputPath);
                    return Fail($"Validación: MA.m[{id}] sigue presente tras eliminar.");
                }
            }

            RufusLog.Ok(
                $"Validación OK · maps_es_{targetVersion}.swf · eliminados {removed} · entradas {check.Entries.Count}");
            return new LangMapsGenerateResult
            {
                Success = true,
                OutputPath = outputPath,
                SourceVersion = sourceVersion,
                TargetVersion = targetVersion,
                Updated = removed > 0,
            };
        }
        catch (Exception ex)
        {
            if (outputPath is not null)
                TryDelete(outputPath);
            RufusLog.Error("Eliminación LANG MapId: " + ex.Message);
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

            var e = matches.FirstOrDefault(m =>
                m.IntProps.GetValueOrDefault("sa") == sa
                && m.IntProps.GetValueOrDefault("ep") == ep);
            if (e is null)
                return $"Validación: MA.m[{mapId}] sa={sa} ep={ep} no encontrada ({matches.Count} variante(s)).";

            if (e.IntProps.GetValueOrDefault("x") != x)
                return $"Validación: X incorrecta ({e.IntProps.GetValueOrDefault("x")} ≠ {x}).";
            if (e.IntProps.GetValueOrDefault("y") != y)
                return $"Validación: Y incorrecta ({e.IntProps.GetValueOrDefault("y")} ≠ {y}).";

            foreach (var other in matches)
            {
                if (other.IntProps.GetValueOrDefault("x") != x
                    || other.IntProps.GetValueOrDefault("y") != y)
                {
                    return $"Validación: MA.m[{mapId}] tiene coords divergentes entre variantes.";
                }
            }

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

    private static int? TryParseMapsEsFileVersion(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name))
            return null;
        const string prefix = "maps_es_";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        return int.TryParse(name.AsSpan(prefix.Length), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }
}
