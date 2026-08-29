using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class DialogEsRemotePublishRequest
{
    public required LangSftpSettings Settings { get; init; }
    public required string PlainPassword { get; init; }
    public required ContentDraftWorkspace Workspace { get; init; }
    public required DialogEsIdOccupancy Occupancy { get; init; }
    public Func<LangSftpSettings, string, ILangSftpPublishClient>? ClientFactory { get; init; }
    public string? WorkDirectory { get; init; }
    public string? BackupDirectory { get; init; }
}

public sealed class DialogEsRemotePublishResult
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
    public DialogEsPublishBatch? Batch { get; init; }
    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// CONT.6C — safe remote dialog_es publish.
/// Order: sync → backup → recalculate → generate → validate → concurrency → upload → hash/parse → versions(dialog only) → verify.
/// Never writes BD. Never deletes SWF. Never touches maps/quests/npc tokens.
/// </summary>
public static class DialogEsRemotePublishService
{
    public static string DefaultWorkDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "dialog-publish");

    public static string DefaultBackupDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "dialog-backups");

    /// <summary>READ path for confirm UI — recalculates IDs, writes nothing remote.</summary>
    public static DialogEsRemotePublishResult PreparePreview(DialogEsRemotePublishRequest request)
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

            var batch = DialogEsPublishBatchBuilder.Build(
                request.Workspace, sync.Snapshot!, request.Occupancy);
            if (!batch.IsValid)
                return PreviewFail(sync, log, string.Join("\n", batch.Errors), batch);

            Ok($"Lote recalculado · D.q={batch.NewQuestionCount} · D.a={batch.NewAnswerCount}");
            return new DialogEsRemotePublishResult
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
            return new DialogEsRemotePublishResult
            {
                Success = false,
                Error = ex.Message,
                StatusLabel = "ERROR",
                LogLines = log,
            };
        }
    }

    public static DialogEsRemotePublishResult Publish(DialogEsRemotePublishRequest request)
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
            // 1. SINCRONIZAR (relectura justo antes de publicar)
            Info("Sincronizando dialog_es activo");
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

            // 2. BACKUP LOCAL
            state.LocalBackupPath = Path.Combine(
                backupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_dialog_v" + n);
            Directory.CreateDirectory(state.LocalBackupPath);
            File.WriteAllBytes(Path.Combine(state.LocalBackupPath, state.SourceSwfFileName!), sync.SourceSwfBytes!);
            File.WriteAllText(
                Path.Combine(state.LocalBackupPath, LangSftpSettings.VersionsFileName),
                sync.VersionsText!,
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(state.LocalBackupPath, "snapshot.txt"),
                $"dialogVersion={n}\nswf={state.SourceSwfFileName}\nswfSha256={sync.SourceSwfSha}\nversionsSha256={sync.VersionsSha}\n",
                Encoding.UTF8);
            Ok("Backup local creado");

            // 3. RECALCULAR IDs (no confiar en provisional UI)
            Info("Recalculando IDs del lote");
            var batch = DialogEsPublishBatchBuilder.Build(
                request.Workspace, sync.Snapshot!, request.Occupancy);
            state.Batch = batch;
            if (!batch.IsValid)
                return Fail(state, log, Err, string.Join("\n", batch.Errors), client);
            Ok($"IDs finales · D.q={batch.NewQuestionCount} · D.a={batch.NewAnswerCount}");

            // 4. GENERAR N+1 LOCAL
            Info("Generando " + state.TargetSwfFileName);
            var genDir = Path.Combine(workRoot, "gen-" + n + "-to-" + (n + 1));
            Directory.CreateDirectory(genDir);
            var gen = DialogEsService.Generate(new DialogEsGenerateRequest
            {
                SourceSwfBytes = sync.SourceSwfBytes!,
                Additions = batch.Additions,
                OutputDirectory = genDir,
            });
            if (!gen.Success || gen.OutputBytes is null || string.IsNullOrWhiteSpace(gen.OutputPath))
                return Fail(state, log, Err, gen.Error ?? "Fallo al generar dialog_es N+1.", client);
            if (gen.TargetVersion != n + 1)
                return Fail(state, log, Err, $"VERSION generada {gen.TargetVersion} != {n + 1}.", client);

            state.LocalGeneratedSwfPath = gen.OutputPath;
            var localBytes = gen.OutputBytes;
            state.LocalSwfSha256 = Sha256Hex(localBytes);

            var valErr = DialogEsService.ValidateGenerated(sync.SourceSwfBytes!, localBytes, batch.Additions);
            if (valErr is not null)
                return Fail(state, log, Err, valErr, client);
            Ok("SWF local validado");

            // 5. CONCURRENCIA
            Info("Comprobando concurrencia");
            var versionsNow = client.ReadAllText(state.VersionsRemote!);
            if (!VersionsEsParser.TryParseDialogVersion(versionsNow, out var nNow, out var nErr))
                return Fail(state, log, Err, nErr ?? "No se pudo re-leer dialog,es,N.", client);

            state.ActiveRemoteVersion = nNow;
            var versionsNowSha = Sha256Hex(Encoding.UTF8.GetBytes(versionsNow));
            if (nNow != n || !string.Equals(versionsNowSha, state.VersionsSha, StringComparison.Ordinal))
            {
                return Fail(
                    state,
                    log,
                    Err,
                    "El dialog_es remoto ha cambiado desde la sincronización. Recalcula antes de publicar.",
                    client);
            }

            if (!client.FileExists(state.SourceSwfRemote!))
                return Fail(state, log, Err, "SWF N desapareció inesperadamente antes del upload.", client);

            var sourceNowBytes = client.DownloadBytes(state.SourceSwfRemote!);
            var sourceNowSha = Sha256Hex(sourceNowBytes);
            if (!string.Equals(sourceNowSha, state.SourceSwfSha, StringComparison.Ordinal))
            {
                return Fail(
                    state,
                    log,
                    Err,
                    "El SWF dialog_es activo cambió desde la lectura usada para generar N+1. Abortado.",
                    client);
            }

            Ok("Remoto sigue en dialog,es," + n);

            // 6. UPLOAD N+1 (sin tocar N)
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

            // 7. VERIFICAR HASH / PARSER REMOTO
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

            DialogEsSnapshot remoteSnap;
            try
            {
                remoteSnap = DialogEsParser.Parse(remoteBytes);
            }
            catch (Exception ex)
            {
                return Fail(
                    state,
                    log,
                    Err,
                    "Parser remoto falló; versions_es NO modificado: " + ex.Message,
                    client);
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

            var remoteVal = DialogEsService.ValidateGenerated(sync.SourceSwfBytes!, remoteBytes, batch.Additions);
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

            // 8. ACTUALIZAR versions_es (solo dialog,es)
            Info("Actualizando versions_es.txt (solo dialog,es)");
            if (!VersionsEsParser.TryBumpDialogVersion(versionsNow, n, n + 1, out var bumped, out var bumpErr))
                return Fail(state, log, Err, bumpErr ?? "No se pudo construir versions_es N+1.", client);

            // Preserve maps token if present
            if (VersionsEsParser.TryParseMapsVersion(versionsNow, out var mapsKeep, out _)
                && VersionsEsParser.TryParseMapsVersion(bumped, out var mapsAfter, out _)
                && mapsKeep != mapsAfter)
            {
                return Fail(state, log, Err, "Abortado: bump dialog alteró maps,es.", client);
            }

            var backupRemoteVersions = Combine(state.LangPath!, VersionsEsParser.VersionsEsEphemeralPrevName);

            client.ReplaceFileAtomically(
                state.VersionsRemote!,
                Encoding.UTF8.GetBytes(bumped),
                backupRemoteVersions);
            state.VersionsUpdated = true;
            Ok($"dialog,es,{n} → dialog,es,{n + 1}");

            // 9. VERIFICACIÓN FINAL
            var finalVersions = client.ReadAllText(state.VersionsRemote!);
            if (!VersionsEsParser.TryParseDialogVersion(finalVersions, out var finalN, out var finalErr)
                || finalN != n + 1)
            {
                state.ActiveRemoteVersion = finalN;
                return Fail(
                    state,
                    log,
                    Err,
                    finalErr ?? $"Verificación final versions_es: dialog,es={finalN}, esperado {n + 1}.",
                    client);
            }

            state.ActiveRemoteVersion = finalN;
            if (!client.FileExists(targetSwfRemote))
                return Fail(state, log, Err, "Verificación final: SWF N+1 inexistente tras actualizar versions_es.", client);
            if (!client.FileExists(state.SourceSwfRemote!))
                return Fail(state, log, Err, "ALERTA: SWF N desapareció tras publicación (no debería borrarse).", client);

            var activeRemote = Combine(state.SwfDir!, VersionsEsParser.BuildDialogSwfFileName(finalN));
            var activeBytes = client.DownloadBytes(activeRemote);
            var activeSnap = DialogEsParser.Parse(activeBytes);
            if (activeSnap.Version != n + 1)
                return Fail(state, log, Err, $"Activo post-publish VERSION={activeSnap.Version}, esperado {n + 1}.", client);
            if (!string.Equals(Sha256Hex(activeBytes), state.RemoteSwfSha256, StringComparison.Ordinal))
                return Fail(state, log, Err, "Hash del SWF activo post-publish distinto del subido.", client);

            var activeVal = DialogEsService.ValidateGenerated(sync.SourceSwfBytes!, activeBytes, batch.Additions);
            if (activeVal is not null)
                return Fail(state, log, Err, "Validación final del activo falló: " + activeVal, client);

            if (client.DeleteAttemptCount != 0)
                return Fail(state, log, Err, "Abortado: se detectaron intentos DELETE remotos.", client);

            DialogEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, batch, finalN);
            DialogEsSessionCache.Shared.Clear();

            Ok("Publicación dialog_es completada");
            return new DialogEsRemotePublishResult
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
        public DialogEsSnapshot? Snapshot;
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
        if (!VersionsEsParser.TryParseDialogVersion(versionsText, out var n, out var parseErr))
        {
            state.Error = parseErr ?? "No se pudo leer dialog,es,N.";
            err(state.Error);
            return state;
        }

        state.SourceVersion = n;
        state.TargetVersion = n + 1;
        state.SourceSwfFileName = VersionsEsParser.BuildDialogSwfFileName(n);
        state.TargetSwfFileName = VersionsEsParser.BuildDialogSwfFileName(n + 1);
        state.VersionsText = versionsText;
        state.VersionsSha = Sha256Hex(Encoding.UTF8.GetBytes(versionsText));
        ok("dialog,es remoto " + n);

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
            state.Snapshot = DialogEsParser.Parse(sourceSwfBytes);
        }
        catch (Exception ex)
        {
            state.Error = "SWF remoto inválido / parser: " + ex.Message;
            err(state.Error);
            return state;
        }

        if (state.Snapshot.Version != n)
        {
            state.Error = $"VERSION interna SWF ({state.Snapshot.Version}) != versions_es dialog,es ({n}).";
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
        public DialogEsPublishBatch? Batch;
    }

    private static DialogEsRemotePublishResult PreviewFail(
        SyncState sync,
        List<string> log,
        string error,
        DialogEsPublishBatch? batch = null) =>
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

    private static DialogEsRemotePublishResult Fail(
        PublishState state,
        List<string> log,
        Action<string> err,
        string error,
        ILangSftpPublishClient? client = null)
    {
        err(error);
        return new DialogEsRemotePublishResult
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
