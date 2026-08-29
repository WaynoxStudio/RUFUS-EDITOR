using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class NpcEsRemotePublishRequest
{
    public required LangSftpSettings Settings { get; init; }
    public required string PlainPassword { get; init; }
    public required ContentDraftWorkspace Workspace { get; init; }
    public Func<LangSftpSettings, string, ILangSftpPublishClient>? ClientFactory { get; init; }
    public string? WorkDirectory { get; init; }
    public string? BackupDirectory { get; init; }
}

public sealed class NpcEsRemotePublishResult
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
    public NpcEsPublishBatch? Batch { get; init; }
    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// CONT.7B — safe remote npc_es publish.
/// Never writes BD. Never deletes SWF. Never touches maps/dialog/quests tokens.
/// </summary>
public static class NpcEsRemotePublishService
{
    public static string DefaultWorkDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "npc-publish");

    public static string DefaultBackupDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "npc-backups");

    public static NpcEsRemotePublishResult PreparePreview(NpcEsRemotePublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var log = new List<string>();
        void Info(string m) { log.Add("INFO " + m); RufusLog.Info(m); }
        void Ok(string m) { log.Add("OK " + m); RufusLog.Ok(m); }
        void Err(string m) { log.Add("ERROR " + m); RufusLog.Error(m); }

        try
        {
            using var client = (request.ClientFactory ?? LangSftpPublishClientFactory.Create)(
                request.Settings, request.PlainPassword);
            client.Connect();
            var sync = SyncActive(client, request.Settings, log, Info, Ok, Err);
            if (sync.Error is not null)
                return PreviewFail(sync, log, sync.Error);

            var batch = NpcEsPublishBatchBuilder.Build(request.Workspace, sync.Snapshot!);
            if (!batch.IsValid)
                return PreviewFail(sync, log, string.Join("\n", batch.Errors), batch);

            Ok($"Lote · nuevos={batch.NewCount} · ya={batch.AlreadyPublished.Count}");
            return new NpcEsRemotePublishResult
            {
                Success = true,
                StatusLabel = "PREVIEW",
                SourceVersion = sync.SourceVersion,
                TargetVersion = sync.TargetVersion,
                SourceSwfFileName = sync.SourceSwfFileName,
                TargetSwfFileName = sync.TargetSwfFileName,
                ActiveRemoteVersion = sync.SourceVersion,
                Batch = batch,
                DeleteAttemptCount = client.DeleteAttemptCount,
                LogLines = log,
            };
        }
        catch (Exception ex)
        {
            Err(ex.Message);
            return new NpcEsRemotePublishResult
            {
                Success = false,
                Error = ex.Message,
                StatusLabel = "ERROR",
                LogLines = log,
            };
        }
    }

    public static NpcEsRemotePublishResult Publish(NpcEsRemotePublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);
        ArgumentNullException.ThrowIfNull(request.Workspace);

        var log = new List<string>();
        void Info(string m) { log.Add("INFO " + m); RufusLog.Info(m); }
        void Ok(string m) { log.Add("OK " + m); RufusLog.Ok(m); }
        void Err(string m) { log.Add("ERROR " + m); RufusLog.Error(m); }

        var state = new PublishState();
        if (string.IsNullOrWhiteSpace(request.Settings.Host) || string.IsNullOrWhiteSpace(request.Settings.User))
            return Fail(state, log, Err, "Configuración SFTP incompleta (Host/Usuario).");

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
            Info("Sincronizando npc_es activo");
            client = factory(request.Settings, request.PlainPassword);
            client.Connect();

            var sync = SyncActive(client, request.Settings, log, Info, Ok, Err);
            if (sync.Error is not null)
                return Fail(state, log, Err, sync.Error, client);

            state.SourceVersion = sync.SourceVersion;
            state.TargetVersion = sync.TargetVersion;
            state.SourceSwfFileName = sync.SourceSwfFileName;
            state.TargetSwfFileName = sync.TargetSwfFileName;
            state.ActiveRemoteVersion = sync.SourceVersion;
            state.SourceSwfBytes = sync.SourceSwfBytes;
            state.VersionsText = sync.VersionsText;
            state.VersionsSha = sync.VersionsSha;
            state.SourceSwfSha = sync.SourceSwfSha;
            state.LangPath = sync.LangPath;
            state.SwfDir = sync.SwfDir;
            state.VersionsRemote = sync.VersionsRemote;
            state.SourceSwfRemote = sync.SourceSwfRemote;

            var n = sync.SourceVersion!.Value;

            state.LocalBackupPath = Path.Combine(
                backupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_npc_v" + n);
            Directory.CreateDirectory(state.LocalBackupPath);
            File.WriteAllBytes(Path.Combine(state.LocalBackupPath, state.SourceSwfFileName!), sync.SourceSwfBytes!);
            File.WriteAllText(
                Path.Combine(state.LocalBackupPath, LangSftpSettings.VersionsFileName),
                sync.VersionsText!,
                Encoding.UTF8);
            Ok("Backup local creado");

            var batch = NpcEsPublishBatchBuilder.Build(request.Workspace, sync.Snapshot!);
            state.Batch = batch;
            if (!batch.IsValid)
                return Fail(state, log, Err, string.Join("\n", batch.Errors), client);

            // Nothing new to upload — mark already-present and exit without SFTP writes.
            if (batch.NewCount == 0)
            {
                NpcEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, batch, n);
                Ok("Nada nuevo que subir; NPCs ya presentes marcados como publicados.");
                return new NpcEsRemotePublishResult
                {
                    Success = true,
                    StatusLabel = "YA_PUBLICADO",
                    SourceVersion = n,
                    TargetVersion = n,
                    SourceSwfFileName = state.SourceSwfFileName,
                    TargetSwfFileName = state.SourceSwfFileName,
                    SwfUploaded = false,
                    VersionsUpdated = false,
                    ActiveRemoteVersion = n,
                    LocalBackupPath = state.LocalBackupPath,
                    DeleteAttemptCount = client.DeleteAttemptCount,
                    Batch = batch,
                    LogLines = log,
                };
            }

            Info("Generando " + state.TargetSwfFileName);
            var genDir = Path.Combine(workRoot, "gen-" + n + "-to-" + (n + 1));
            Directory.CreateDirectory(genDir);
            var gen = NpcEsService.Generate(new NpcEsGenerateRequest
            {
                SourceSwfBytes = sync.SourceSwfBytes!,
                Additions = batch.Additions,
                OutputDirectory = genDir,
            });
            if (!gen.Success || gen.OutputBytes is null || string.IsNullOrWhiteSpace(gen.OutputPath))
                return Fail(state, log, Err, gen.Error ?? "Fallo al generar npc_es N+1.", client);
            if (gen.TargetVersion != n + 1)
                return Fail(state, log, Err, $"VERSION generada {gen.TargetVersion} != {n + 1}.", client);

            state.LocalGeneratedSwfPath = gen.OutputPath;
            var localBytes = gen.OutputBytes;
            state.LocalSwfSha256 = Sha256Hex(localBytes);

            var valErr = NpcEsService.ValidateGenerated(sync.SourceSwfBytes!, localBytes, batch.Additions);
            if (valErr is not null)
                return Fail(state, log, Err, valErr, client);
            Ok("SWF local validado");

            Info("Comprobando concurrencia");
            var versionsNow = client.ReadAllText(state.VersionsRemote!);
            if (!VersionsEsParser.TryParseNpcVersion(versionsNow, out var nNow, out var nErr))
                return Fail(state, log, Err, nErr ?? "No se pudo re-leer npc,es,N.", client);

            state.ActiveRemoteVersion = nNow;
            var versionsNowSha = Sha256Hex(Encoding.UTF8.GetBytes(versionsNow));
            if (nNow != n || !string.Equals(versionsNowSha, state.VersionsSha, StringComparison.Ordinal))
            {
                return Fail(
                    state,
                    log,
                    Err,
                    "El npc_es remoto ha cambiado desde la sincronización. Recalcula antes de publicar.",
                    client);
            }

            if (!client.FileExists(state.SourceSwfRemote!))
                return Fail(state, log, Err, "SWF N desapareció inesperadamente antes del upload.", client);

            var sourceNowBytes = client.DownloadBytes(state.SourceSwfRemote!);
            if (!string.Equals(Sha256Hex(sourceNowBytes), state.SourceSwfSha, StringComparison.Ordinal))
            {
                return Fail(
                    state,
                    log,
                    Err,
                    "El SWF npc_es activo cambió desde la lectura usada para generar N+1. Abortado.",
                    client);
            }

            Ok("Remoto sigue en npc,es," + n);

            var targetSwfRemote = Combine(state.SwfDir!, state.TargetSwfFileName!);
            if (client.FileExists(targetSwfRemote))
            {
                return Fail(
                    state,
                    log,
                    Err,
                    $"SWF destino ya existe en remoto ({state.TargetSwfFileName}). No se sobrescribe automáticamente.",
                    client);
            }

            Info("Subiendo " + state.TargetSwfFileName);
            client.UploadNewFile(targetSwfRemote, localBytes);
            state.SwfUploaded = true;

            var remoteBytes = client.DownloadBytes(targetSwfRemote);
            if (remoteBytes.Length != localBytes.Length)
            {
                return Fail(
                    state,
                    log,
                    Err,
                    $"Tamaño remoto distinto ({remoteBytes.Length} vs {localBytes.Length}). versions_es NO modificado.",
                    client);
            }

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

            NpcEsSnapshot remoteSnap;
            try
            {
                remoteSnap = NpcEsParser.Parse(remoteBytes);
            }
            catch (Exception ex)
            {
                return Fail(state, log, Err, "Parser remoto falló; versions_es NO modificado: " + ex.Message, client);
            }

            if (remoteSnap.Version != n + 1)
            {
                return Fail(
                    state,
                    log,
                    Err,
                    $"VERSION interna remota ({remoteSnap.Version}) != {n + 1}. versions_es NO modificado.",
                    client);
            }

            var remoteVal = NpcEsService.ValidateGenerated(sync.SourceSwfBytes!, remoteBytes, batch.Additions);
            if (remoteVal is not null)
            {
                return Fail(
                    state,
                    log,
                    Err,
                    "Validación semántica remota falló; versions_es NO modificado: " + remoteVal,
                    client);
            }

            Ok("Hash + parser remoto verificados");

            Info("Actualizando versions_es.txt (solo npc,es)");
            if (!VersionsEsParser.TryBumpNpcVersion(versionsNow, n, n + 1, out var bumped, out var bumpErr))
                return Fail(state, log, Err, bumpErr ?? "No se pudo construir versions_es N+1.", client);

            if (VersionsEsParser.TryParseDialogVersion(versionsNow, out var dialogKeep, out _)
                && VersionsEsParser.TryParseDialogVersion(bumped, out var dialogAfter, out _)
                && dialogKeep != dialogAfter)
            {
                return Fail(state, log, Err, "Abortado: bump npc alteró dialog,es.", client);
            }

            if (VersionsEsParser.TryParseMapsVersion(versionsNow, out var mapsKeep, out _)
                && VersionsEsParser.TryParseMapsVersion(bumped, out var mapsAfter, out _)
                && mapsKeep != mapsAfter)
            {
                return Fail(state, log, Err, "Abortado: bump npc alteró maps,es.", client);
            }

            var backupRemoteVersions = Combine(state.LangPath!, VersionsEsParser.VersionsEsEphemeralPrevName);

            client.ReplaceFileAtomically(
                state.VersionsRemote!,
                Encoding.UTF8.GetBytes(bumped),
                backupRemoteVersions);
            state.VersionsUpdated = true;
            Ok($"npc,es,{n} → npc,es,{n + 1}");

            var finalVersions = client.ReadAllText(state.VersionsRemote!);
            if (!VersionsEsParser.TryParseNpcVersion(finalVersions, out var finalN, out var finalErr)
                || finalN != n + 1)
            {
                state.ActiveRemoteVersion = finalN;
                return Fail(
                    state,
                    log,
                    Err,
                    finalErr ?? $"Verificación final versions_es: npc,es={finalN}, esperado {n + 1}.",
                    client);
            }

            state.ActiveRemoteVersion = finalN;
            if (!client.FileExists(targetSwfRemote))
                return Fail(state, log, Err, "Verificación final: SWF N+1 inexistente tras actualizar versions_es.", client);
            if (!client.FileExists(state.SourceSwfRemote!))
                return Fail(state, log, Err, "ALERTA: SWF N desapareció tras publicación (no debería borrarse).", client);

            var activeRemote = Combine(state.SwfDir!, VersionsEsParser.BuildNpcSwfFileName(finalN));
            var activeBytes = client.DownloadBytes(activeRemote);
            var activeSnap = NpcEsParser.Parse(activeBytes);
            if (activeSnap.Version != n + 1)
                return Fail(state, log, Err, $"Activo post-publish VERSION={activeSnap.Version}, esperado {n + 1}.", client);
            if (!string.Equals(Sha256Hex(activeBytes), state.RemoteSwfSha256, StringComparison.Ordinal))
                return Fail(state, log, Err, "Hash del SWF activo post-publish distinto del subido.", client);

            foreach (var add in batch.Additions)
            {
                if (!activeSnap.Names.TryGetValue(add.Id, out var nm)
                    || !string.Equals(nm, add.Name, StringComparison.Ordinal))
                {
                    return Fail(
                        state,
                        log,
                        Err,
                        $"Verificación final: N.d[{add.Id}].n incorrecto o ausente.",
                        client);
                }

                if (!NpcEsClientActions.SameSet(activeSnap.ActionsOf(add.Id), add.Actions))
                {
                    return Fail(
                        state,
                        log,
                        Err,
                        $"Verificación final: N.d[{add.Id}].a incorrecto.",
                        client);
                }
            }

            if (client.DeleteAttemptCount != 0)
                return Fail(state, log, Err, "Abortado: se detectaron intentos DELETE remotos.", client);

            NpcEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, batch, finalN);

            Ok("Publicación npc_es completada");
            return new NpcEsRemotePublishResult
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
                Batch = batch,
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

    private sealed class SyncState
    {
        public string? Error;
        public int? SourceVersion;
        public int? TargetVersion;
        public string? SourceSwfFileName;
        public string? TargetSwfFileName;
        public byte[]? SourceSwfBytes;
        public string? VersionsText;
        public string? VersionsSha;
        public string? SourceSwfSha;
        public string? LangPath;
        public string? SwfDir;
        public string? VersionsRemote;
        public string? SourceSwfRemote;
        public NpcEsSnapshot? Snapshot;
    }

    private static SyncState SyncActive(
        ILangSftpPublishClient client,
        LangSftpSettings settings,
        List<string> log,
        Action<string> info,
        Action<string> ok,
        Action<string> err)
    {
        _ = log;
        var state = new SyncState();
        var langPath = NormalizeDir(BlankTo(settings.LangRemotePath, LangSftpSettings.DefaultLangRemotePath));
        var swfDir = NormalizeDir(BlankTo(settings.SwfRemotePath, LangSftpSettings.DefaultSwfRemotePath));
        state.LangPath = langPath;
        state.SwfDir = swfDir;

        var versionsRemote = Combine(langPath, LangSftpSettings.VersionsFileName);
        state.VersionsRemote = versionsRemote;
        if (!client.FileExists(versionsRemote))
        {
            state.Error = "versions_es.txt inexistente en remoto.";
            err(state.Error);
            return state;
        }

        var versionsText = client.ReadAllText(versionsRemote);
        if (!VersionsEsParser.TryParseNpcVersion(versionsText, out var n, out var parseErr))
        {
            state.Error = parseErr ?? "No se pudo leer npc,es,N.";
            err(state.Error);
            return state;
        }

        state.SourceVersion = n;
        state.TargetVersion = n + 1;
        state.SourceSwfFileName = VersionsEsParser.BuildNpcSwfFileName(n);
        state.TargetSwfFileName = VersionsEsParser.BuildNpcSwfFileName(n + 1);
        state.VersionsText = versionsText;
        state.VersionsSha = Sha256Hex(Encoding.UTF8.GetBytes(versionsText));
        ok("npc,es remoto " + n);

        var sourceSwfRemote = Combine(swfDir, state.SourceSwfFileName);
        state.SourceSwfRemote = sourceSwfRemote;
        if (!client.FileExists(sourceSwfRemote))
        {
            state.Error = "SWF activo inexistente: " + state.SourceSwfFileName;
            err(state.Error);
            return state;
        }

        info("Descargando " + state.SourceSwfFileName);
        var sourceSwfBytes = client.DownloadBytes(sourceSwfRemote);
        state.SourceSwfBytes = sourceSwfBytes;
        state.SourceSwfSha = Sha256Hex(sourceSwfBytes);

        try
        {
            state.Snapshot = NpcEsParser.Parse(sourceSwfBytes);
        }
        catch (Exception ex)
        {
            state.Error = "SWF remoto inválido / parser: " + ex.Message;
            err(state.Error);
            return state;
        }

        if (state.Snapshot.Version != n)
        {
            state.Error = $"VERSION interna SWF ({state.Snapshot.Version}) != versions_es npc,es ({n}).";
            err(state.Error);
            return state;
        }

        return state;
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
        public byte[]? SourceSwfBytes;
        public string? VersionsText;
        public string? VersionsSha;
        public string? SourceSwfSha;
        public string? LangPath;
        public string? SwfDir;
        public string? VersionsRemote;
        public string? SourceSwfRemote;
        public NpcEsPublishBatch? Batch;
    }

    private static NpcEsRemotePublishResult PreviewFail(
        SyncState sync,
        List<string> log,
        string error,
        NpcEsPublishBatch? batch = null) =>
        new()
        {
            Success = false,
            Error = error,
            StatusLabel = "ERROR",
            SourceVersion = sync.SourceVersion,
            TargetVersion = sync.TargetVersion,
            SourceSwfFileName = sync.SourceSwfFileName,
            TargetSwfFileName = sync.TargetSwfFileName,
            ActiveRemoteVersion = sync.SourceVersion,
            Batch = batch,
            LogLines = log,
        };

    private static NpcEsRemotePublishResult Fail(
        PublishState state,
        List<string> log,
        Action<string> err,
        string error,
        ILangSftpPublishClient? client = null)
    {
        err(error);
        return new NpcEsRemotePublishResult
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
            Batch = state.Batch,
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
