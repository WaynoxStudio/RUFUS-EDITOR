using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT-CONN.1 — shared connection state for Content UI (reuses Mapas config).</summary>
public enum SharedConnectionState
{
    Unchecked = 0,
    Connected = 1,
    Error = 2,
    NotConfigured = 3,
}

/// <summary>
/// CONT-CONN.1 — READ-ONLY probes using the same DatabaseSettings / LangSftpSettings as Mapas.
/// Never writes BD, versions_es, or SWF.
/// </summary>
public static class ContentSharedConnectionProbe
{
    public static async Task ProbeDatabaseAsync(
        DatabaseSettings settings,
        string plainPassword,
        Func<DatabaseSettings, string, IMapasRepository>? repositoryFactory = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            throw new InvalidOperationException("Configura primero la BD (misma configuración que Mapas).");

        RufusLog.Info($"Contenido · BD comprobando · {settings.Host}:{settings.Port} · usuario {settings.User}");
        var factory = repositoryFactory ?? ((s, p) => new MysqlMapasRepository(s, p));
        var repo = factory(settings, plainPassword);
        await repo.TestConnectionAsync(ct).ConfigureAwait(false);
        RufusLog.Ok("Contenido · BD conectada");
    }

    public static string ProbeSftp(
        LangSftpSettings settings,
        string plainPassword,
        Func<LangSftpSettings, string, ILangSftpReadClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            throw new InvalidOperationException("Configura primero LANG / SFTP (misma configuración que Mapas).");

        return LangRemoteSyncService.TestLangPathsAccess(settings, plainPassword, clientFactory);
    }

    public static string FormatStateLabel(SharedConnectionState state, bool database) =>
        state switch
        {
            SharedConnectionState.Connected => database ? "● Conectada" : "● Conectado",
            SharedConnectionState.Error => "● Error",
            SharedConnectionState.NotConfigured => "● Sin configurar",
            _ => "● Sin comprobar",
        };

    public static bool IsDatabaseConfigured(DatabaseSettings? settings) =>
        settings is not null
        && !string.IsNullOrWhiteSpace(settings.Host)
        && !string.IsNullOrWhiteSpace(settings.User);

    public static bool IsSftpConfigured(LangSftpSettings? settings) =>
        settings is not null
        && !string.IsNullOrWhiteSpace(settings.Host)
        && !string.IsNullOrWhiteSpace(settings.User);

    public static string SanitizeError(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("password", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
            return "Acceso denegado (usuario/contraseña o permisos).";
        if (msg.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Conexión SFTP fallida", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Conexion SFTP fallida", StringComparison.OrdinalIgnoreCase))
            return "No se pudo conectar al servidor.";
        // Never echo raw passwords if somehow present.
        if (msg.Contains("Pwd=", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Password=", StringComparison.OrdinalIgnoreCase))
            return "Error de conexión (detalles omitidos).";
        return msg;
    }
}
