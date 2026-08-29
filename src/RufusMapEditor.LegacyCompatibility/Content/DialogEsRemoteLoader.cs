using System.Globalization;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.6B.2 — READ-ONLY fetch of the active remote dialog_es. Never writes SFTP/BD/versions_es.</summary>
public sealed class DialogEsRemoteLoadRequest
{
    public required LangSftpSettings Settings { get; init; }
    public required string PlainPassword { get; init; }
    public Func<LangSftpSettings, string, ILangSftpReadClient>? ClientFactory { get; init; }
    /// <summary>Working directory for a session copy. Must not be a user document path.</summary>
    public string? WorkDirectory { get; init; }
}

public sealed class DialogEsRemoteLoadResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public bool ConnectionOk { get; init; }
    public int? DialogVersion { get; init; }
    public string? Token { get; init; }
    public string? RemoteSwfPath { get; init; }
    public string? LocalTempPath { get; init; }
    public DialogEsSnapshot? Snapshot { get; init; }
    public int RemoteWriteAttempts { get; init; }

    public string StatusLabel => Success
        ? string.Create(CultureInfo.InvariantCulture, $"dialog_es activo remoto: {Token}")
        : CannotCalculateMessage + (string.IsNullOrWhiteSpace(Error) ? "" : "\n" + Error);

    public const string CannotCalculateMessage = "⚠ No se puede calcular ID dialog_es";
}

/// <summary>In-process cache of the last successful/failed remote load. Future publish must force a fresh read.</summary>
public sealed class DialogEsSessionCache
{
    public static DialogEsSessionCache Shared { get; } = new();

    private readonly object _gate = new();
    private DialogEsRemoteLoadResult? _last;

    public DialogEsRemoteLoadResult? Last
    {
        get { lock (_gate) return _last; }
    }

    public void Clear()
    {
        lock (_gate) _last = null;
    }

    public DialogEsRemoteLoadResult GetOrFetch(DialogEsRemoteLoadRequest request, bool forceRemote)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (!forceRemote && _last is not null)
                return _last;
            _last = DialogEsRemoteLoader.Fetch(request);
            return _last;
        }
    }
}

public static class DialogEsRemoteLoader
{
    public static string DefaultWorkDirectory =>
        Path.Combine(Path.GetTempPath(), "RufusMapEditor", "dialog-es-work");

    public static DialogEsRemoteLoadResult Fetch(DialogEsRemoteLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        if (string.IsNullOrWhiteSpace(request.Settings.Host) || string.IsNullOrWhiteSpace(request.Settings.User))
        {
            return Fail("SFTP no configurado (misma conexión que Mapas).", connectionOk: false);
        }

        var factory = request.ClientFactory ?? LangSftpReadClientFactory.Create;
        var workDir = string.IsNullOrWhiteSpace(request.WorkDirectory)
            ? DefaultWorkDirectory
            : request.WorkDirectory!;

        ILangSftpReadClient? client = null;
        try
        {
            RufusLog.Info($"dialog_es · SFTP READ-ONLY · {request.Settings.Host}:{request.Settings.Port}");
            client = factory(request.Settings, request.PlainPassword);
            client.Connect();
            if (client.WriteAttemptCount != 0)
                return Fail("Cliente SFTP escribió al conectar.", connectionOk: true, writes: client.WriteAttemptCount);

            var langPath = NormalizeDir(string.IsNullOrWhiteSpace(request.Settings.LangRemotePath)
                ? LangSftpSettings.DefaultLangRemotePath
                : request.Settings.LangRemotePath);
            var swfDir = NormalizeDir(string.IsNullOrWhiteSpace(request.Settings.SwfRemotePath)
                ? LangSftpSettings.DefaultSwfRemotePath
                : request.Settings.SwfRemotePath);
            var versionsRemote = Combine(langPath, LangSftpSettings.VersionsFileName);

            if (!client.FileExists(versionsRemote))
                return Fail("No se pudo leer versions_es.txt (inexistente).", connectionOk: true, writes: client.WriteAttemptCount);

            var versionsText = client.ReadAllText(versionsRemote);
            if (client.WriteAttemptCount != 0)
                return Fail("Cliente SFTP escribió al leer versions_es.", connectionOk: true, writes: client.WriteAttemptCount);

            if (!VersionsEsParser.TryParseDialogVersion(versionsText, out var dialogVersion, out var parseErr))
                return Fail(parseErr ?? "No se pudo leer dialog,es,N.", connectionOk: true, writes: client.WriteAttemptCount);

            var token = string.Create(CultureInfo.InvariantCulture, $"dialog,es,{dialogVersion}");
            var swfName = VersionsEsParser.BuildDialogSwfFileName(dialogVersion);
            var swfRemote = Combine(swfDir, swfName);
            if (!client.FileExists(swfRemote))
            {
                return Fail(
                    $"dialog_es activo inexistente en remoto: {swfName}",
                    connectionOk: true,
                    writes: client.WriteAttemptCount,
                    version: dialogVersion,
                    token: token,
                    remote: swfRemote);
            }

            var remoteLen = client.GetFileLength(swfRemote);
            var bytes = client.DownloadBytes(swfRemote);
            if (client.WriteAttemptCount != 0)
                return Fail("Cliente SFTP escribió al descargar dialog_es.", connectionOk: true, writes: client.WriteAttemptCount);
            if (bytes.Length == 0 || bytes.Length != remoteLen)
            {
                return Fail(
                    $"Descarga dialog_es incompleta (remoto={remoteLen}, local={bytes.Length}).",
                    connectionOk: true,
                    writes: client.WriteAttemptCount,
                    version: dialogVersion,
                    token: token,
                    remote: swfRemote);
            }

            Directory.CreateDirectory(workDir);
            var localPath = Path.Combine(workDir, swfName);
            File.WriteAllBytes(localPath, bytes);

            DialogEsSnapshot snap;
            try
            {
                snap = DialogEsParser.Parse(bytes);
            }
            catch (Exception ex)
            {
                return Fail(
                    "No se pudo parsear dialog_es activo: " + ex.Message,
                    connectionOk: true,
                    writes: client.WriteAttemptCount,
                    version: dialogVersion,
                    token: token,
                    remote: swfRemote,
                    local: localPath);
            }

            if (snap.Version != dialogVersion)
            {
                return Fail(
                    $"VERSION interna ({snap.Version}) distinta de versions_es dialog,es ({dialogVersion}).",
                    connectionOk: true,
                    writes: client.WriteAttemptCount,
                    version: dialogVersion,
                    token: token,
                    remote: swfRemote,
                    local: localPath);
            }

            RufusLog.Ok($"dialog_es_{dialogVersion}.swf leído (solo lectura)");
            return new DialogEsRemoteLoadResult
            {
                Success = true,
                ConnectionOk = true,
                DialogVersion = dialogVersion,
                Token = token,
                RemoteSwfPath = swfRemote,
                LocalTempPath = localPath,
                Snapshot = snap,
                RemoteWriteAttempts = client.WriteAttemptCount,
            };
        }
        catch (Exception ex)
        {
            return Fail(
                "SFTP no disponible: " + ex.Message,
                connectionOk: false,
                writes: client?.WriteAttemptCount ?? 0);
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static DialogEsRemoteLoadResult Fail(
        string error,
        bool connectionOk,
        int writes = 0,
        int? version = null,
        string? token = null,
        string? remote = null,
        string? local = null) =>
        new()
        {
            Success = false,
            Error = error,
            ConnectionOk = connectionOk,
            DialogVersion = version,
            Token = token,
            RemoteSwfPath = remote,
            LocalTempPath = local,
            RemoteWriteAttempts = writes,
        };

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
}
