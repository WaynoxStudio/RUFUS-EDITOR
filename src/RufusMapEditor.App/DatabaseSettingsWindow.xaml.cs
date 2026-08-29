using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.App;

public partial class DatabaseSettingsWindow : Window
{
    private readonly AppSettings _settings;

    public DatabaseSettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ThemeService.ApplyToWindow(this);

        var db = settings.Database ?? new DatabaseSettings();
        HostBox.Text = db.Host;
        PortBox.Text = db.Port.ToString();
        UserBox.Text = db.User;
        DatabaseBox.Text = string.IsNullOrWhiteSpace(db.Database) ? MapasColumns.DefaultDatabase : db.Database;
        TableBox.Text = string.IsNullOrWhiteSpace(db.Table) ? MapasColumns.DefaultTable : db.Table;
        PasswordBox.Password = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
        var d = db.NewMapDefaults ?? new NewMapDefaultsSettings();
        DefaultKeyBox.Text = d.Key ?? "";
        DefaultMobsBox.Text = d.Mobs ?? "";
        DefaultSubAreaBox.Text = d.SubArea?.ToString() ?? "";
        DefaultMaxGrupoMobsBox.Text = d.MaxGrupoMobs?.ToString() ?? "";
        DefaultMaxMobsPorGrupoBox.Text = d.MaxMobsPorGrupo?.ToString() ?? "";
        DefaultMinNivelGrupoMobBox.Text = d.MinNivelGrupoMob?.ToString() ?? "";
        DefaultMaxNivelGrupoMobBox.Text = d.MaxNivelGrupoMob?.ToString() ?? "";
        DefaultMaxMercantesBox.Text = d.MaxMercantes?.ToString() ?? "";
        DefaultMaxPeleasBox.Text = d.MaxPeleas?.ToString() ?? "";
        DefaultMinMobsPorGrupoBox.Text = d.MinMobsPorGrupo?.ToString() ?? "";
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Probando…";
        try
        {
            var cfg = ReadSettings(out var password);
            RufusLog.Info($"Conexión BD iniciada · {cfg.Host}:{cfg.Port} · usuario {cfg.User}");
            var repo = new MysqlMapasRepository(cfg, password);
            repo.TestConnectionAsync().GetAwaiter().GetResult();
            StatusText.Text = "Conexión correcta";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
            RufusLog.Ok("Conexión BD correcta");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + FriendlyDbError(ex);
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            RufusLog.Error("Conexión BD fallida: " + FriendlyDbError(ex));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = ReadSettings(out var password);
            cfg.PasswordProtectedBase64 = string.IsNullOrEmpty(password)
                ? null
                : DatabasePasswordProtector.Protect(password);
            _settings.Database = cfg;
            AppSettingsStore.Save(_settings);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, FriendlyDbError(ex), "Configuración BD", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private DatabaseSettings ReadSettings(out string password)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
            throw new InvalidOperationException("Puerto inválido.");

        password = PasswordBox.Password ?? "";
        return new DatabaseSettings
        {
            Host = HostBox.Text.Trim(),
            Port = port,
            User = UserBox.Text.Trim(),
            Database = string.IsNullOrWhiteSpace(DatabaseBox.Text) ? MapasColumns.DefaultDatabase : DatabaseBox.Text.Trim(),
            Table = string.IsNullOrWhiteSpace(TableBox.Text) ? MapasColumns.DefaultTable : TableBox.Text.Trim(),
            PasswordProtectedBase64 = _settings.Database?.PasswordProtectedBase64,
            NewMapDefaults = new NewMapDefaultsSettings
            {
                Key = OptionalText(DefaultKeyBox),
                Mobs = OptionalText(DefaultMobsBox),
                SubArea = OptionalInt(DefaultSubAreaBox, MapasColumns.SubArea),
                MaxGrupoMobs = OptionalInt(DefaultMaxGrupoMobsBox, MapasColumns.MaxGrupoMobs),
                MaxMobsPorGrupo = OptionalInt(DefaultMaxMobsPorGrupoBox, MapasColumns.MaxMobsPorGrupo),
                MinNivelGrupoMob = OptionalInt(DefaultMinNivelGrupoMobBox, MapasColumns.MinNivelGrupoMob),
                MaxNivelGrupoMob = OptionalInt(DefaultMaxNivelGrupoMobBox, MapasColumns.MaxNivelGrupoMob),
                MaxMercantes = OptionalInt(DefaultMaxMercantesBox, MapasColumns.MaxMercantes),
                MaxPeleas = OptionalInt(DefaultMaxPeleasBox, MapasColumns.MaxPeleas),
                MinMobsPorGrupo = OptionalInt(DefaultMinMobsPorGrupoBox, MapasColumns.MinMobsPorGrupo),
            },
        };
    }

    private static string? OptionalText(TextBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text;

    private static int? OptionalInt(TextBox box, string field)
    {
        if (string.IsNullOrWhiteSpace(box.Text))
            return null;
        if (!int.TryParse(box.Text.Trim(), out var value))
            throw new InvalidOperationException($"{field}: entero inválido.");
        return value;
    }

    internal static string FriendlyDbError(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("password", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
            return "Acceso denegado (usuario/contraseña o permisos).";
        if (msg.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase))
            return "No se pudo conectar al servidor MySQL.";
        return msg;
    }
}
