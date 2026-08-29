using System.Diagnostics;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Swf;

public sealed class SwfExportRequest
{
    public required MapDocument Document { get; init; }
    public required string DestinationSwfPath { get; init; }
    public required string FlasmExePath { get; init; }
    public required string BlankSwfTemplatePath { get; init; }
    public int TimeoutMs { get; init; } = FlasmProcessRunner.DefaultTimeoutMs;
}

public sealed class SwfExportResult
{
    public required bool Success { get; init; }
    public required string DestinationPath { get; init; }
    public required string MapDataExported { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public string? ErrorMessage { get; init; }
    public FlasmRunResult? FlasmAssemble { get; init; }
    public FlasmSwfMetadataReader.SwfMapMetadata? ReadBack { get; init; }
    public string? ReadBackMapData { get; init; }
    public long OutputBytes { get; init; }
}

/// <summary>
/// Exports MapDocument → Astria-compatible SWF via Flasm assembly on a copied blank.swf.
/// Never writes into the Astria installation; all work happens in a private temp folder.
/// </summary>
public static class SwfMapExporter
{
    public static string? ResolveFlasmExe(string libraryRoot) =>
        FirstExisting(
            Path.Combine(libraryRoot, "Flasm", "flasm.exe"),
            Path.Combine(libraryRoot, "flasm.exe"));

    public static string? ResolveBlankSwf(string libraryRoot) =>
        FirstExisting(
            Path.Combine(libraryRoot, "Flasm", "blank.swf"),
            Path.Combine(libraryRoot, "blank.swf"));

    public static SwfExportResult Export(SwfExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var map = request.Document ?? throw new SwfExportException("Documento nulo.");
        var dest = Path.GetFullPath(request.DestinationSwfPath);
        var swTotal = Stopwatch.StartNew();

        try
        {
            FlasmScriptBuilder.ValidateForExport(map);

            if (!File.Exists(request.FlasmExePath))
                throw new SwfExportException($"Flasm no encontrado: {request.FlasmExePath}");
            if (!File.Exists(request.BlankSwfTemplatePath))
                throw new SwfExportException($"Plantilla SWF no encontrada: {request.BlankSwfTemplatePath}");

            var blankInfo = new FileInfo(request.BlankSwfTemplatePath);
            if (blankInfo.Length < 100)
                throw new SwfExportException(
                    $"Plantilla blank.swf corrupta o demasiado pequeña ({blankInfo.Length} bytes; Astria espera >100).");

            MapCellEditor.SyncMapDataString(map);
            var expectedLen = MapGeometry.ExpectedMapDataLength(map.Width, map.Height);
            if (map.MapData.Length != expectedLen)
                throw new SwfExportException(
                    $"MapData inválido: longitud {map.MapData.Length}, esperado {expectedLen}.");

            var flm = FlasmScriptBuilder.Build(map, "blank.swf");

            var work = Path.Combine(Path.GetTempPath(), "RufusMapEditor", "swf-export", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var blankWork = Path.Combine(work, "blank.swf");
            var flmPath = Path.Combine(work, "temp.flm");
            var assembledPath = blankWork;
            var backupPath = Path.Combine(work, "blank.$wf");

            try
            {
                File.Copy(request.BlankSwfTemplatePath, blankWork, overwrite: true);
                File.WriteAllText(flmPath, flm);

                var assemble = FlasmProcessRunner.Run(
                    request.FlasmExePath,
                    new[] { "-a", "temp.flm" },
                    work,
                    request.TimeoutMs,
                    cancellationToken);

                if (assemble.TimedOut)
                    throw new SwfExportException($"Flasm agotó el tiempo de espera ({request.TimeoutMs} ms).");
                if (assemble.ExitCode != 0)
                    throw new SwfExportException(
                        $"Flasm devolvió exit code {assemble.ExitCode}: {TrimMsg(assemble.StdErr)}{TrimMsg(assemble.StdOut)}");
                if (!File.Exists(assembledPath) || new FileInfo(assembledPath).Length == 0)
                    throw new SwfExportException("SWF generado no puede volver a leerse (archivo vacío o ausente tras Flasm).");
                if (!File.Exists(backupPath))
                    throw new SwfExportException(
                        "Flasm no produjo blank.$wf — compruebe que blank.swf sea una plantilla válida.");

                // Post-export validation: disassemble + compare
                var read = FlasmSwfMetadataReader.Read(assembledPath, request.FlasmExePath, includeMapData: true);
                var mismatches = Compare(map, read);
                if (mismatches.Count > 0)
                    throw new SwfExportException(
                        "SWF generado no puede volver a leerse de forma coherente:\n" + string.Join("\n", mismatches));

                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                var destTmp = dest + ".tmp";
                File.Copy(assembledPath, destTmp, overwrite: true);
                if (File.Exists(dest))
                    File.Replace(destTmp, dest, destinationBackupFileName: null);
                else
                    File.Move(destTmp, dest);

                swTotal.Stop();
                return new SwfExportResult
                {
                    Success = true,
                    DestinationPath = dest,
                    MapDataExported = map.MapData,
                    Elapsed = swTotal.Elapsed,
                    FlasmAssemble = assemble,
                    ReadBack = read,
                    ReadBackMapData = read.MapData,
                    OutputBytes = new FileInfo(dest).Length,
                };
            }
            finally
            {
                TryDeleteDir(work);
            }
        }
        catch (SwfExportException ex)
        {
            swTotal.Stop();
            return Fail(dest, map, swTotal.Elapsed, ex.Message);
        }
        catch (Exception ex)
        {
            swTotal.Stop();
            return Fail(dest, map, swTotal.Elapsed, ex.Message);
        }
    }

    public static List<string> Compare(MapDocument map, FlasmSwfMetadataReader.SwfMapMetadata meta)
    {
        var list = new List<string>();
        if (meta.Id != map.Id) list.Add($"id: doc={map.Id} swf={meta.Id}");
        if (meta.Width != map.Width) list.Add($"width: doc={map.Width} swf={meta.Width}");
        if (meta.Height != map.Height) list.Add($"height: doc={map.Height} swf={meta.Height}");
        if (meta.BackgroundNum != map.BackgroundId) list.Add($"backgroundNum: doc={map.BackgroundId} swf={meta.BackgroundNum}");
        if (meta.AmbianceId != map.AmbianceId) list.Add($"ambianceId: doc={map.AmbianceId} swf={meta.AmbianceId}");
        if (meta.MusicId != map.MusicId) list.Add($"musicId: doc={map.MusicId} swf={meta.MusicId}");
        if (meta.Capabilities != map.Capabilities) list.Add($"capabilities: doc={map.Capabilities} swf={meta.Capabilities}");
        var outdoor = map.Outdoor ?? false;
        if (meta.Outdoor != outdoor) list.Add($"bOutdoor: doc={outdoor} swf={meta.Outdoor}");
        if (!string.Equals(meta.MapData, map.MapData, StringComparison.Ordinal))
            list.Add($"mapData: length doc={map.MapData.Length} swf={meta.MapData.Length} (contenido distinto)");
        return list;
    }

    private static SwfExportResult Fail(string dest, MapDocument map, TimeSpan elapsed, string msg) =>
        new()
        {
            Success = false,
            DestinationPath = dest,
            MapDataExported = map.MapData ?? "",
            Elapsed = elapsed,
            ErrorMessage = msg,
        };

    private static string TrimMsg(string s) =>
        string.IsNullOrWhiteSpace(s) ? "" : " " + s.Trim();

    private static string? FirstExisting(params string[] paths) =>
        paths.FirstOrDefault(File.Exists);

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }
}
