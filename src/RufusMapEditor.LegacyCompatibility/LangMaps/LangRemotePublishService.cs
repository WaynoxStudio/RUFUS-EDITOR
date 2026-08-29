using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

public sealed class LangRemotePublishRequest
{
    public required LangSftpSettings Settings { get; init; }
    public required string PlainPassword { get; init; }
    public required int MapId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int SubArea { get; init; }
    /// <summary>Obligatorio y explicito. Nunca inventado.</summary>
    public required int Ep { get; init; }
    public Func<LangSftpSettings, string, ILangSftpPublishClient>? ClientFactory { get; init; }
    public string? WorkDirectory { get; init; }
    public string? BackupDirectory { get; init; }
}

/// <summary>MAP-BATCH.1 — one maps_es N+1 for many maps; one versions_es bump.</summary>
public sealed class LangRemoteBatchPublishRequest
{
    public required LangSftpSettings Settings { get; init; }
    public required string PlainPassword { get; init; }
    public required IReadOnlyList<LangMapsBatchEntry> Entries { get; init; }
    public Func<LangSftpSettings, string, ILangSftpPublishClient>? ClientFactory { get; init; }
    public string? WorkDirectory { get; init; }
    public string? BackupDirectory { get; init; }
}

public sealed class LangRemotePublishResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string StatusLabel { get; init; } = "";

    public int? SourceVersion { get; init; }
    public int? TargetVersion { get; init; }
    public string? SourceSwfFileName { get; init; }
    public string? TargetSwfFileName { get; init; }

    public bool SwfUploaded { get; init; }
    public bool VersionsUpdated { get; init; }
    public int? ActiveRemoteVersion { get; init; }

    public string? LocalBackupPath { get; init; }
    public string? LocalGeneratedSwfPath { get; init; }
    public string? LocalSwfSha256 { get; init; }
    public string? RemoteSwfSha256 { get; init; }

    public int DeleteAttemptCount { get; init; }
    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// FASE 11B.2 — publicacion LANG remota segura.
/// Orden fijo: sync → backup → generate → validate → concurrency → upload → hash → versions → verify.
/// No toca BD. No DELETE de SWF. No sobrescribe N ni N+1 existente.
/// </summary>
public static class LangRemotePublishService
{
    public static string DefaultWorkDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "lang-publish");

    public static string DefaultBackupDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "lang-backups");

    public static LangRemotePublishResult Publish(LangRemotePublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PublishBatch(new LangRemoteBatchPublishRequest
        {
            Settings = request.Settings,
            PlainPassword = request.PlainPassword,
            Entries =
            [
                new LangMapsBatchEntry
                {
                    MapId = request.MapId,
                    X = request.X,
                    Y = request.Y,
                    SubArea = request.SubArea,
                    Ep = request.Ep,
                },
            ],
            ClientFactory = request.ClientFactory,
            WorkDirectory = request.WorkDirectory,
            BackupDirectory = request.BackupDirectory,
        });
    }

    /// <summary>
    /// MAP-BATCH.1 — same safety pipeline as single publish, one SWF N+1 for the whole lote.
    /// </summary>
    public static LangRemotePublishResult PublishBatch(LangRemoteBatchPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        var log = new List<string>();
        void Info(string m) { log.Add("INFO " + m); RufusLog.Info(m); }
        void Ok(string m) { log.Add("OK " + m); RufusLog.Ok(m); }
        void Err(string m) { log.Add("ERROR " + m); RufusLog.Error(m); }

        var state = new PublishState();

        if (request.Entries is null || request.Entries.Count == 0)
            return Fail(state, log, Err, "Lote LANG vacío.");
        if (request.Entries.Any(e => e.MapId <= 0))
            return Fail(state, log, Err, "MapId invalido en el lote.");
        if (string.IsNullOrWhiteSpace(request.Settings.Host) || string.IsNullOrWhiteSpace(request.Settings.User))
            return Fail(state, log, Err, "Configuracion SFTP incompleta (Host/Usuario).");

        var workRoot = string.IsNullOrWhiteSpace(request.WorkDirectory)
            ? DefaultWorkDirectory
            : request.WorkDirectory!;
        var backupRoot = string.IsNullOrWhiteSpace(request.BackupDirectory)
            ? DefaultBackupDirectory
            : request.BackupDirectory!;
        Directory.CreateDirectory(workRoot);
        Directory.CreateDirectory(backupRoot);

        var factory = request.ClientFactory ?? LangSftpPublishClientFactory.Create;
        ILangSftpPublishClient? client = null;

        try
        {
            // 1. SINCRONIZAR
            Info("Sincronizando LANG (lote " + request.Entries.Count + ")");
            client = factory(request.Settings, request.PlainPassword);
            client.Connect();

            var langPath = NormalizeDir(BlankTo(
                request.Settings.LangRemotePath, LangSftpSettings.DefaultLangRemotePath));
            var swfDir = NormalizeDir(BlankTo(
                request.Settings.SwfRemotePath, LangSftpSettings.DefaultSwfRemotePath));

            var versionsRemote = Combine(langPath, LangSftpSettings.VersionsFileName);
            if (!client.FileExists(versionsRemote))
                return Fail(state, log, Err, "versions_es.txt inexistente en remoto.", client);

            var versionsText = client.ReadAllText(versionsRemote);
            if (!VersionsEsParser.TryParseMapsVersion(versionsText, out var n, out var parseErr))
                return Fail(state, log, Err, parseErr ?? "No se pudo leer maps,es,N.", client);

            state.SourceVersion = n;
            state.TargetVersion = n + 1;
            state.SourceSwfFileName = VersionsEsParser.BuildSwfFileName(n);
            state.TargetSwfFileName = VersionsEsParser.BuildSwfFileName(n + 1);
            state.ActiveRemoteVersion = n;
            Ok("VERSION remota " + n);

            var sourceSwfRemote = Combine(swfDir, state.SourceSwfFileName);
            if (!client.FileExists(sourceSwfRemote))
                return Fail(state, log, Err, "SWF activo inexistente: " + state.SourceSwfFileName, client);

            var sourceSwfBytes = client.DownloadBytes(sourceSwfRemote);
            var versionsSha = Sha256Hex(Encoding.UTF8.GetBytes(versionsText));
            var sourceSwfSha = Sha256Hex(sourceSwfBytes);

            var syncCache = Path.Combine(
                workRoot,
                "sync-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(syncCache);
            var cachedSource = Path.Combine(syncCache, state.SourceSwfFileName);
            File.WriteAllBytes(cachedSource, sourceSwfBytes);
            File.WriteAllText(
                Path.Combine(syncCache, LangSftpSettings.VersionsFileName),
                versionsText,
                Encoding.UTF8);

            LangMapsInspectResult inspect;
            try
            {
                inspect = LangMapsSwfService.Inspect(cachedSource);
            }
            catch (Exception ex)
            {
                return Fail(state, log, Err, "SWF remoto invalido / parser 11A: " + ex.Message, client);
            }

            if (inspect.Version != n)
                return Fail(state, log, Err, $"VERSION interna SWF ({inspect.Version}) != versions_es ({n}).", client);
            if (inspect.EntryCount <= 0)
                return Fail(state, log, Err, "MA.m no legible / sin entradas.", client);

            var snapshotVersion = n;
            var snapshotVersionsSha = versionsSha;

            // 2. BACKUP LOCAL
            state.LocalBackupPath = Path.Combine(
                backupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_v" + n + "_batch");
            Directory.CreateDirectory(state.LocalBackupPath);
            File.Copy(cachedSource, Path.Combine(state.LocalBackupPath, state.SourceSwfFileName), overwrite: true);
            File.WriteAllText(
                Path.Combine(state.LocalBackupPath, LangSftpSettings.VersionsFileName),
                versionsText,
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(state.LocalBackupPath, "snapshot.txt"),
                $"mapsVersion={n}\nswf={state.SourceSwfFileName}\nswfSha256={sourceSwfSha}\nversionsSha256={versionsSha}\nbatch={request.Entries.Count}\n",
                Encoding.UTF8);
            Ok("Backup local creado");

            // 3. GENERAR N+1 (todos los mapas del lote)
            Info("Generando " + state.TargetSwfFileName + " (" + request.Entries.Count + " mapas)");
            var genDir = Path.Combine(workRoot, "gen-batch-" + n + "-to-" + (n + 1));
            Directory.CreateDirectory(genDir);
            var gen = LangMapsSwfService.GenerateBatch(new LangMapsBatchGenerateRequest
            {
                SourceSwfPath = cachedSource,
                OutputDirectory = genDir,
                Entries = request.Entries,
            });
            if (!gen.Success || string.IsNullOrWhiteSpace(gen.OutputPath) || !File.Exists(gen.OutputPath))
                return Fail(state, log, Err, gen.Error ?? "Fallo al generar SWF N+1 del lote.", client);

            state.LocalGeneratedSwfPath = gen.OutputPath;
            if (gen.TargetVersion != n + 1)
                return Fail(state, log, Err, $"VERSION generada {gen.TargetVersion} != {n + 1}.", client);

            // 4. VALIDACION LOCAL (cada mapa)
            foreach (var entry in request.Entries)
            {
                var valErr = LangMapsSwfService.ValidateGenerated(
                    state.LocalGeneratedSwfPath!,
                    n + 1,
                    entry.MapId,
                    entry.X,
                    entry.Y,
                    entry.SubArea,
                    entry.Ep);
                if (valErr is not null)
                    return Fail(state, log, Err, valErr, client);
            }

            var localBytes = File.ReadAllBytes(state.LocalGeneratedSwfPath!);
            state.LocalSwfSha256 = Sha256Hex(localBytes);
            Ok("SWF local validado (lote)");

            // 5. CONCURRENCIA
            Info("Comprobando concurrencia");
            var versionsNow = client.ReadAllText(versionsRemote);
            if (!VersionsEsParser.TryParseMapsVersion(versionsNow, out var nNow, out var nErr))
                return Fail(state, log, Err, nErr ?? "No se pudo re-leer maps,es,N.", client);

            state.ActiveRemoteVersion = nNow;
            var versionsNowSha = Sha256Hex(Encoding.UTF8.GetBytes(versionsNow));
            if (nNow != snapshotVersion
                || !string.Equals(versionsNowSha, snapshotVersionsSha, StringComparison.Ordinal))
            {
                return Fail(
                    state,
                    log,
                    Err,
                    "El LANG remoto ha cambiado desde la sincronizacion. Sincroniza de nuevo antes de publicar.",
                    client);
            }

            Ok("Remoto sigue en " + n);

            // 6. UPLOAD N+1 (sin tocar N)
            var targetSwfRemote = Combine(swfDir, state.TargetSwfFileName!);
            if (client.FileExists(targetSwfRemote))
            {
                return Fail(
                    state,
                    log,
                    Err,
                    $"SWF destino ya existe en remoto ({state.TargetSwfFileName}). No se sobrescribe automaticamente.",
                    client);
            }

            if (!client.FileExists(sourceSwfRemote))
                return Fail(state, log, Err, "SWF N desaparecio inesperadamente antes del upload.", client);

            Info("Subiendo " + state.TargetSwfFileName);
            client.UploadNewFile(targetSwfRemote, localBytes);
            state.SwfUploaded = true;

            // 7. VERIFICAR HASH / VERSION REMOTA
            var remoteBytes = client.DownloadBytes(targetSwfRemote);
            state.RemoteSwfSha256 = Sha256Hex(remoteBytes);
            if (!string.Equals(state.LocalSwfSha256, state.RemoteSwfSha256, StringComparison.Ordinal))
            {
                return Fail(
                    state,
                    log,
                    Err,
                    $"Hash remoto distinto del local. versions_es NO modificado. Remoto sigue en N={n}.",
                    client);
            }

            var remoteCheckPath = Path.Combine(workRoot, "remote-verify-" + state.TargetSwfFileName);
            File.WriteAllBytes(remoteCheckPath, remoteBytes);
            var remoteInspect = LangMapsSwfService.Inspect(remoteCheckPath);
            if (remoteInspect.Version != n + 1)
            {
                return Fail(
                    state,
                    log,
                    Err,
                    $"VERSION interna remota ({remoteInspect.Version}) != {n + 1}. versions_es NO modificado.",
                    client);
            }

            Ok("Hash remoto verificado");

            // 8. ACTUALIZAR versions_es (solo maps,es) — UNA vez
            Info("Actualizando versions_es.txt");
            if (!VersionsEsParser.TryBumpMapsVersion(versionsNow, n, n + 1, out var bumped, out var bumpErr))
                return Fail(state, log, Err, bumpErr ?? "No se pudo construir versions_es N+1.", client);

            var backupRemoteVersions = Combine(langPath, VersionsEsParser.VersionsEsEphemeralPrevName);

            client.ReplaceFileAtomically(
                versionsRemote,
                Encoding.UTF8.GetBytes(bumped),
                backupRemoteVersions);
            state.VersionsUpdated = true;
            Ok($"maps,es,{n} → maps,es,{n + 1}");

            // 9. VERIFICACION FINAL
            var finalVersions = client.ReadAllText(versionsRemote);
            if (!VersionsEsParser.TryParseMapsVersion(finalVersions, out var finalN, out var finalErr)
                || finalN != n + 1)
            {
                state.ActiveRemoteVersion = finalN;
                return Fail(
                    state,
                    log,
                    Err,
                    finalErr ?? $"Verificacion final versions_es: maps,es={finalN}, esperado {n + 1}.",
                    client);
            }

            state.ActiveRemoteVersion = finalN;
            if (!client.FileExists(targetSwfRemote))
                return Fail(state, log, Err, "Verificacion final: SWF N+1 inexistente tras actualizar versions_es.", client);
            if (!client.FileExists(sourceSwfRemote))
                return Fail(state, log, Err, "ALERTA: SWF N desaparecio tras publicacion (no deberia borrarse).", client);
            if (client.DeleteAttemptCount != 0)
                return Fail(state, log, Err, "Abortado: se detectaron intentos DELETE remotos.", client);

            Ok("Publicacion LANG lote completada");
            return new LangRemotePublishResult
            {
                Success = true,
                StatusLabel = "PUBLICADO",
                SourceVersion = state.SourceVersion,
                TargetVersion = state.TargetVersion,
                SourceSwfFileName = state.SourceSwfFileName,
                TargetSwfFileName = state.TargetSwfFileName,
                SwfUploaded = true,
                VersionsUpdated = true,
                ActiveRemoteVersion = finalN,
                LocalBackupPath = state.LocalBackupPath,
                LocalGeneratedSwfPath = state.LocalGeneratedSwfPath,
                LocalSwfSha256 = state.LocalSwfSha256,
                RemoteSwfSha256 = state.RemoteSwfSha256,
                DeleteAttemptCount = client.DeleteAttemptCount,
                LogLines = log,
            };
        }
        catch (Exception ex)
        {
            return Fail(state, log, Err, ex.Message, client);
        }
        finally
        {
            client?.Dispose();
        }
    }

    private sealed class PublishState
    {
        public int? SourceVersion;
        public int? TargetVersion;
        public string? SourceSwfFileName;
        public string? TargetSwfFileName;
        public bool SwfUploaded;
        public bool VersionsUpdated;
        public int? ActiveRemoteVersion;
        public string? LocalBackupPath;
        public string? LocalGeneratedSwfPath;
        public string? LocalSwfSha256;
        public string? RemoteSwfSha256;
    }

    private static LangRemotePublishResult Fail(
        PublishState state,
        List<string> log,
        Action<string> err,
        string error,
        ILangSftpPublishClient? client = null)
    {
        err(error);
        return new LangRemotePublishResult
        {
            Success = false,
            Error = error,
            StatusLabel = "ERROR",
            SourceVersion = state.SourceVersion,
            TargetVersion = state.TargetVersion,
            SourceSwfFileName = state.SourceSwfFileName,
            TargetSwfFileName = state.TargetSwfFileName,
            SwfUploaded = state.SwfUploaded,
            VersionsUpdated = state.VersionsUpdated,
            ActiveRemoteVersion = state.ActiveRemoteVersion,
            LocalBackupPath = state.LocalBackupPath,
            LocalGeneratedSwfPath = state.LocalGeneratedSwfPath,
            LocalSwfSha256 = state.LocalSwfSha256,
            RemoteSwfSha256 = state.RemoteSwfSha256,
            DeleteAttemptCount = client?.DeleteAttemptCount ?? 0,
            LogLines = log,
        };
    }

    private static string BlankTo(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string NormalizeDir(string path)
    {
        var p = path.Replace('\\', '/').Trim();
        if (!p.EndsWith('/'))
            p += '/';
        return p;
    }

    private static string Combine(string dir, string file)
    {
        dir = NormalizeDir(dir);
        return dir + file.TrimStart('/');
    }

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
