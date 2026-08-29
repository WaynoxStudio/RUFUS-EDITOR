using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.Admin.Services;

/// <summary>
/// ADMIN.UI.3.1 — shared BD/SFTP status for the Admin shell (same AppSettings as Mapas/Contenido).
/// Never stores or displays passwords.
/// </summary>
public sealed class AdminInfrastructureStatus
{
    private bool _checking;

    public SharedConnectionState DatabaseState { get; private set; } = SharedConnectionState.Unchecked;
    public SharedConnectionState SftpState { get; private set; } = SharedConnectionState.Unchecked;
    public string DatabaseDetail { get; private set; } = "";
    public string SftpDetail { get; private set; } = "";
    public bool IsChecking => _checking;

    public event Action? Changed;

    public string DatabaseLabel => ContentSharedConnectionProbe.FormatStateLabel(DatabaseState, database: true);
    public string SftpLabel => ContentSharedConnectionProbe.FormatStateLabel(SftpState, database: false);

    public async Task CheckAllAsync()
    {
        if (_checking)
            return;
        _checking = true;
        Raise();
        try
        {
            await CheckDatabaseAsync().ConfigureAwait(true);
            await CheckSftpAsync().ConfigureAwait(true);
        }
        finally
        {
            _checking = false;
            Raise();
        }
    }

    public async Task CheckDatabaseAsync()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            var db = settings.Database ?? new DatabaseSettings();
            if (!ContentSharedConnectionProbe.IsDatabaseConfigured(db))
            {
                DatabaseState = SharedConnectionState.NotConfigured;
                DatabaseDetail = "Configura la BD en Mapas / Ajustes del Editor.";
                Raise();
                return;
            }

            string password;
            try
            {
                password = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
            }
            catch (Exception ex)
            {
                DatabaseState = SharedConnectionState.Error;
                DatabaseDetail = ContentSharedConnectionProbe.SanitizeError(ex);
                Raise();
                return;
            }

            await ContentSharedConnectionProbe.ProbeDatabaseAsync(db, password).ConfigureAwait(false);
            DatabaseState = SharedConnectionState.Connected;
            DatabaseDetail = $"{db.Host}:{db.Port} · {db.User} · {db.Database}";
        }
        catch (Exception ex)
        {
            DatabaseState = SharedConnectionState.Error;
            DatabaseDetail = ContentSharedConnectionProbe.SanitizeError(ex);
        }

        Raise();
    }

    public async Task CheckSftpAsync()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            var sftp = settings.LangSftp ?? new LangSftpSettings();
            if (!ContentSharedConnectionProbe.IsSftpConfigured(sftp))
            {
                SftpState = SharedConnectionState.NotConfigured;
                SftpDetail = "Configura LANG/SFTP en Mapas / Ajustes del Editor.";
                Raise();
                return;
            }

            string password;
            try
            {
                password = LangSftpPasswordProtector.Unprotect(sftp.PasswordProtectedBase64);
            }
            catch (Exception ex)
            {
                SftpState = SharedConnectionState.Error;
                SftpDetail = ContentSharedConnectionProbe.SanitizeError(ex);
                Raise();
                return;
            }

            var message = await Task.Run(() => ContentSharedConnectionProbe.ProbeSftp(sftp, password))
                .ConfigureAwait(false);
            SftpState = SharedConnectionState.Connected;
            SftpDetail = $"{sftp.Host}:{sftp.Port} · {sftp.User} · {message}";
        }
        catch (Exception ex)
        {
            SftpState = SharedConnectionState.Error;
            SftpDetail = ContentSharedConnectionProbe.SanitizeError(ex);
        }

        Raise();
    }

    private void Raise() => Changed?.Invoke();
}
