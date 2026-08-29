using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.App;

public partial class LangSftpSettingsWindow : Window
{
    private readonly AppSettings _settings;

    public LangSftpSettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ThemeService.ApplyToWindow(this);

        var s = settings.LangSftp ?? new LangSftpSettings();
        HostBox.Text = s.Host;
        PortBox.Text = (s.Port <= 0 ? 22 : s.Port).ToString();
        UserBox.Text = s.User;
        PasswordBox.Password = LangSftpPasswordProtector.Unprotect(s.PasswordProtectedBase64);
        LangPathBox.Text = string.IsNullOrWhiteSpace(s.LangRemotePath)
            ? LangSftpSettings.DefaultLangRemotePath
            : s.LangRemotePath;
        SwfPathBox.Text = string.IsNullOrWhiteSpace(s.SwfRemotePath)
            ? LangSftpSettings.DefaultSwfRemotePath
            : s.SwfRemotePath;
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Probando…";
        try
        {
            var cfg = ReadSettings(out var password);
            StatusText.Text = LangRemoteSyncService.TestConnection(cfg, password);
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + Friendly(ex);
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            RufusLog.Error("SFTP prueba de conexión: " + Friendly(ex));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = ReadSettings(out var password);
            cfg.PasswordProtectedBase64 = string.IsNullOrEmpty(password)
                ? null
                : LangSftpPasswordProtector.Protect(password);
            cfg.LastSync = _settings.LangSftp?.LastSync;
            _settings.LangSftp = cfg;
            AppSettingsStore.Save(_settings);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Friendly(ex), "Configuración LANG / SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private LangSftpSettings ReadSettings(out string password)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
            throw new InvalidOperationException("Puerto inválido.");

        password = PasswordBox.Password ?? "";
        return new LangSftpSettings
        {
            Host = HostBox.Text.Trim(),
            Port = port,
            User = UserBox.Text.Trim(),
            PasswordProtectedBase64 = _settings.LangSftp?.PasswordProtectedBase64,
            LangRemotePath = string.IsNullOrWhiteSpace(LangPathBox.Text)
                ? LangSftpSettings.DefaultLangRemotePath
                : LangPathBox.Text.Trim(),
            SwfRemotePath = string.IsNullOrWhiteSpace(SwfPathBox.Text)
                ? LangSftpSettings.DefaultSwfRemotePath
                : SwfPathBox.Text.Trim(),
        };
    }

    private static string Friendly(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("password", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Autenticación", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
            return "Autenticación SFTP fallida.";
        return msg;
    }
}
