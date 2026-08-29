using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

public sealed class LangRemoteSyncRequest
{
    public required LangSftpSettings Settings { get; init; }
    public required string PlainPassword { get; init; }
    public Func<LangSftpSettings, string, ILangSftpReadClient>? ClientFactory { get; init; }
    public string? CacheDirectory { get; init; }
}

public sealed class LangRemoteSyncResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public bool ConnectionOk { get; init; }
    public int? MapsVersion { get; init; }
    public string? SwfFileName { get; init; }
    public int? InternalVersion { get; init; }
    public int? MaEntryCount { get; init; }
    public string? LocalCachePath { get; init; }
    public string? SwfSha256 { get; init; }
    public string? VersionsEsSha256 { get; init; }
    public string? VersionsEsMapsLine { get; init; }
    public bool VersionsMatch { get; init; }
    public string StatusLabel { get; init; } = "";
    public LangRemoteSyncSnapshot? Snapshot { get; init; }
    public int RemoteWriteAttempts { get; init; }
}

/// <summary>FASE 11B.1 — sincronización READ-ONLY + validación con parser 11A.</summary>
public static class LangRemoteSyncService
{
    public static string DefaultCacheDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "lang-cache");

    public static string TestConnection(
        LangSftpSettings settings,
        string plainPassword,
        Func<LangSftpSettings, string, ILangSftpReadClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RufusLog.Info($"SFTP conectando a {settings.Host}:{settings.Port} como {settings.User}");
        var factory = clientFactory ?? LangSftpReadClientFactory.Create;
        using var client = factory(settings, plainPassword);
        client.Connect();
        if (client.WriteAttemptCount != 0)
            throw new InvalidOperationException("Cliente SFTP realizó escrituras en prueba de conexión.");
        RufusLog.Ok("SFTP conexión correcta");
        return "Conexión SFTP correcta";
    }

    /// <summary>
    /// CONT-CONN.1 — READ-ONLY check: connect + access lang/ and swf/ directories.
    /// Never writes versions_es or SWF.
    /// </summary>
    public static string TestLangPathsAccess(
        LangSftpSettings settings,
        string plainPassword,
        Func<LangSftpSettings, string, ILangSftpReadClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RufusLog.Info($"SFTP (Contenido) comprobando rutas · {settings.Host}:{settings.Port} como {settings.User}");
        var factory = clientFactory ?? LangSftpReadClientFactory.Create;
        using var client = factory(settings, plainPassword);
        client.Connect();

        var langPath = NormalizeDir(string.IsNullOrWhiteSpace(settings.LangRemotePath)
            ? LangSftpSettings.DefaultLangRemotePath
            : settings.LangRemotePath);
        var swfPath = NormalizeDir(string.IsNullOrWhiteSpace(settings.SwfRemotePath)
            ? LangSftpSettings.DefaultSwfRemotePath
            : settings.SwfRemotePath);

        if (!client.DirectoryExists(langPath))
            throw new InvalidOperationException("Sin acceso de lectura a " + langPath);
        if (!client.DirectoryExists(swfPath))
            throw new InvalidOperationException("Sin acceso de lectura a " + swfPath);
        if (client.WriteAttemptCount != 0)
            throw new InvalidOperationException("Cliente SFTP realizó escrituras en prueba de rutas.");

        RufusLog.Ok("SFTP rutas lang/swf accesibles (solo lectura)");
        return "SFTP OK · acceso a lang/ y swf/";
    }

    public static LangRemoteSyncResult Sync(LangRemoteSyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        var settings = request.Settings;
        var factory = request.ClientFactory ?? LangSftpReadClientFactory.Create;
        var cacheRoot = string.IsNullOrWhiteSpace(request.CacheDirectory)
            ? DefaultCacheDirectory
            : request.CacheDirectory!;

        ILangSftpReadClient? client = null;
        try
        {
            RufusLog.Info($"SFTP conectando a {settings.Host}:{settings.Port} como {settings.User}");
            client = factory(settings, request.PlainPassword);
            client.Connect();
            RufusLog.Ok("SFTP conectado");

            var langPath = NormalizeDir(string.IsNullOrWhiteSpace(settings.LangRemotePath)
                ? LangSftpSettings.DefaultLangRemotePath
                : settings.LangRemotePath);
            var swfDir = NormalizeDir(string.IsNullOrWhiteSpace(settings.SwfRemotePath)
                ? LangSftpSettings.DefaultSwfRemotePath
                : settings.SwfRemotePath);

            var versionsRemote = Combine(langPath, LangSftpSettings.VersionsFileName);
            if (!client.FileExists(versionsRemote))
            {
                RufusLog.Error("versions_es.txt inexistente en ruta LANG remota");
                return Fail("versions_es.txt inexistente en la ruta LANG remota.", connectionOk: true);
            }

            var versionsText = client.ReadAllText(versionsRemote);
            RufusLog.Info("Lectura versions_es completada");
            if (!VersionsEsParser.TryParseMapsVersion(versionsText, out var mapsVersion, out var parseError))
            {
                RufusLog.Error(parseError ?? "No se pudo leer maps,es,N.");
                return Fail(parseError ?? "No se pudo leer maps,es,N.", connectionOk: true);
            }

            RufusLog.Info($"versions_es detectado: {mapsVersion}");
            var swfName = VersionsEsParser.BuildSwfFileName(mapsVersion);
            var swfRemote = Combine(swfDir, swfName);
            if (!client.FileExists(swfRemote))
            {
                RufusLog.Error($"SWF activo inexistente en remoto: {swfName}");
                return Fail(
                    $"SWF activo inexistente en remoto: {swfName}",
                    connectionOk: true,
                    mapsVersion: mapsVersion,
                    swfFileName: swfName);
            }

            var expectedLen = client.GetFileLength(swfRemote);
            var swfBytes = client.DownloadBytes(swfRemote);
            if (expectedLen > 0 && swfBytes.LongLength != expectedLen)
            {
                RufusLog.Error($"Descarga incompleta: remoto={expectedLen} bytes, local={swfBytes.LongLength} bytes");
                return Fail(
                    $"Descarga incompleta: remoto={expectedLen} bytes, local={swfBytes.LongLength} bytes.",
                    connectionOk: true,
                    mapsVersion: mapsVersion,
                    swfFileName: swfName);
            }

            Directory.CreateDirectory(cacheRoot);
            var cachePath = Path.Combine(cacheRoot, swfName);
            File.WriteAllBytes(cachePath, swfBytes);
            RufusLog.Ok($"{swfName} descargado");

            LangMapsInspectResult inspect;
            try
            {
                inspect = LangMapsSwfService.Inspect(cachePath);
            }
            catch (Exception ex)
            {
                TryDelete(cachePath);
                RufusLog.Error("SWF inválido / parser 11A: " + ex.Message);
                return Fail(
                    "SWF inválido / parser 11A: " + ex.Message,
                    connectionOk: true,
                    mapsVersion: mapsVersion,
                    swfFileName: swfName);
            }

            var internalVersion = inspect.Version;
            var versionsMatch = internalVersion == mapsVersion;
            var swfHash = Sha256Hex(swfBytes);
            var versionsHash = Sha256Hex(Encoding.UTF8.GetBytes(versionsText));
            var mapsLine = VersionsEsParser.ExtractMapsLine(versionsText);
            RufusLog.Info($"Hash SWF SHA256={swfHash[..Math.Min(12, swfHash.Length)]}…");

            if (!versionsMatch)
            {
                RufusLog.Error(
                    $"VERSION interna ({internalVersion}) distinta de versions_es.txt ({mapsVersion})");
                return new LangRemoteSyncResult
                {
                    Success = false,
                    ConnectionOk = true,
                    Error =
                        $"VERSION interna ({internalVersion}) distinta de versions_es.txt ({mapsVersion}). " +
                        "No se intentará corregir el servidor.",
                    MapsVersion = mapsVersion,
                    SwfFileName = swfName,
                    InternalVersion = internalVersion,
                    MaEntryCount = inspect.EntryCount,
                    LocalCachePath = cachePath,
                    SwfSha256 = swfHash,
                    VersionsEsSha256 = versionsHash,
                    VersionsEsMapsLine = mapsLine,
                    VersionsMatch = false,
                    StatusLabel = "DESINCRONIZADO (versión)",
                    RemoteWriteAttempts = client.WriteAttemptCount,
                };
            }

            if (client.WriteAttemptCount != 0)
            {
                RufusLog.Error("Abortado: intentos de escritura remota detectados");
                return Fail(
                    "Abortado: el cliente SFTP registró intentos de escritura remota.",
                    connectionOk: true,
                    mapsVersion: mapsVersion,
                    swfFileName: swfName);
            }

            var snapshot = new LangRemoteSyncSnapshot
            {
                MapsVersion = mapsVersion,
                SwfFileName = swfName,
                SwfSha256 = swfHash,
                VersionsEsSha256 = versionsHash,
                VersionsEsRelevantLine = mapsLine,
                SyncedUtc = DateTimeOffset.UtcNow,
                LocalCachePath = cachePath,
            };

            RufusLog.Ok($"Sincronización LANG OK · maps_es_{mapsVersion}");
            return new LangRemoteSyncResult
            {
                Success = true,
                ConnectionOk = true,
                MapsVersion = mapsVersion,
                SwfFileName = swfName,
                InternalVersion = internalVersion,
                MaEntryCount = inspect.EntryCount,
                LocalCachePath = cachePath,
                SwfSha256 = swfHash,
                VersionsEsSha256 = versionsHash,
                VersionsEsMapsLine = mapsLine,
                VersionsMatch = true,
                StatusLabel = "SINCRONIZADO",
                Snapshot = snapshot,
                RemoteWriteAttempts = 0,
            };
        }
        catch (Exception ex)
        {
            RufusLog.Error("Error de sincronización SFTP: " + ex.Message);
            return Fail(ex.Message, connectionOk: false);
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static LangRemoteSyncResult Fail(
        string error,
        bool connectionOk,
        int? mapsVersion = null,
        string? swfFileName = null) =>
        new()
        {
            Success = false,
            Error = error,
            ConnectionOk = connectionOk,
            MapsVersion = mapsVersion,
            SwfFileName = swfFileName,
            StatusLabel = "ERROR",
            RemoteWriteAttempts = 0,
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

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }
}
