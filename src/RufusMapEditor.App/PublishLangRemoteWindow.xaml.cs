using System.Globalization;
using System.Text;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.MapPublishQueue;

namespace RufusMapEditor.App;

public partial class PublishLangRemoteWindow : Window
{
    private readonly AppSettings _settings;
    private readonly int _mapId;
    private bool _busy;

    public PublishLangRemoteWindow(
        AppSettings settings,
        int mapId,
        int? worldX,
        int? worldY,
        int? subArea)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        _settings = settings;
        _mapId = mapId;

        MapIdBox.Text = mapId.ToString(CultureInfo.InvariantCulture);
        XBox.Text = worldX?.ToString(CultureInfo.InvariantCulture) ?? "";
        YBox.Text = worldY?.ToString(CultureInfo.InvariantCulture) ?? "";
        SubAreaBox.Text = subArea?.ToString(CultureInfo.InvariantCulture) ?? "";
        EpBox.Text = MapPublishQueueItem.DefaultEp.ToString(CultureInfo.InvariantCulture);
        EpConfirmBox.IsChecked = false;

        RefreshSummary();
        StatusText.Text = $"EP predeterminado = {MapPublishQueueItem.DefaultEp}. Confirme EP y pulse Publicar LANG.";
    }

    private void RefreshSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("PUBLICAR LANG");
        sb.AppendLine();
        sb.AppendLine($"Map ID:          {_mapId}");
        sb.AppendLine($"X:               {XBox.Text}");
        sb.AppendLine($"Y:               {YBox.Text}");
        sb.AppendLine($"SubArea:         {SubAreaBox.Text}");
        sb.AppendLine($"EP:              {(string.IsNullOrWhiteSpace(EpBox.Text) ? "(pendiente)" : EpBox.Text)}");

        var sync = _settings.LangSftp?.LastSync;
        if (sync is null)
        {
            sb.AppendLine();
            sb.AppendLine("Estado sincronizacion: SIN SNAPSHOT");
            sb.AppendLine("Ejecute primero «Sincronizar LANG remoto».");
        }
        else
        {
            var n = sync.MapsVersion;
            sb.AppendLine();
            sb.AppendLine($"Version actual:  {n}");
            sb.AppendLine($"Nueva version:   {n + 1}");
            sb.AppendLine();
            sb.AppendLine("Actual:");
            sb.AppendLine("  " + sync.SwfFileName);
            sb.AppendLine("Nuevo:");
            sb.AppendLine("  " + VersionsEsParser.BuildSwfFileName(n + 1));
            sb.AppendLine();
            sb.AppendLine($"Estado sincronizacion: OK (UTC {sync.SyncedUtc:yyyy-MM-dd HH:mm})");
        }

        SummaryBox.Text = sb.ToString();
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        RefreshSummary();

        _settings.LangSftp ??= new LangSftpSettings();
        var cfg = _settings.LangSftp;
        if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.User))
        {
            MessageBox.Show(this, "Configure primero LANG / SFTP.", Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (cfg.LastSync is null)
        {
            MessageBox.Show(this,
                "No hay snapshot de sincronizacion.\nEjecute primero «Sincronizar LANG remoto».",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(XBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(YBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(SubAreaBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sa))
        {
            MessageBox.Show(this, "X, Y y SubArea deben ser enteros.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(EpBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ep))
        {
            MessageBox.Show(this,
                LangMapsSwfService.EpUndefinedMessage + "\nIndique EP explicitamente.",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (EpConfirmBox.IsChecked != true)
        {
            MessageBox.Show(this,
                "Debe confirmar explicitamente el valor EP antes de publicar.",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string password;
        try
        {
            password = LangSftpPasswordProtector.Unprotect(cfg.PasswordProtectedBase64);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo descifrar la contraseña SFTP.\n" + ex.Message,
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "No hay contraseña SFTP guardada.", Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var n = cfg.LastSync.MapsVersion;
        var confirm = MessageBox.Show(
            this,
            $"Se publicara LANG remoto:\n\n" +
            $"Map {_mapId} · x={x} y={y} sa={sa} ep={ep}\n" +
            $"maps,es,{n} → maps,es,{n + 1}\n" +
            $"{VersionsEsParser.BuildSwfFileName(n)} → {VersionsEsParser.BuildSwfFileName(n + 1)}\n\n" +
            "Esto ESCRIBIRA en el VPS (SWF nuevo + versions_es).\n" +
            "No toca la BD.\n\n¿Continuar?",
            Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
            return;

        _busy = true;
        PublishButton.IsEnabled = false;
        BusyBar.Visibility = Visibility.Visible;
        StatusText.Text = "Publicando LANG remoto…";
        RufusLog.Info($"Publicacion LANG iniciada · mapa {_mapId} · {n}→{n + 1}");

        LangRemotePublishResult result;
        try
        {
            result = await Task.Run(() => LangRemotePublishService.Publish(new LangRemotePublishRequest
            {
                Settings = cfg,
                PlainPassword = password,
                MapId = _mapId,
                X = x,
                Y = y,
                SubArea = sa,
                Ep = ep,
            })).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _busy = false;
            PublishButton.IsEnabled = true;
            BusyBar.Visibility = Visibility.Collapsed;
            StatusText.Text = ex.Message;
            RufusLog.Error("Publicacion LANG: " + ex.Message);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _busy = false;
        PublishButton.IsEnabled = true;
        BusyBar.Visibility = Visibility.Collapsed;
        StatusText.Text = FormatResult(result);

        if (result.Success)
        {
            cfg.LastSync = new LangRemoteSyncSnapshot
            {
                MapsVersion = result.TargetVersion ?? (n + 1),
                SwfFileName = result.TargetSwfFileName ?? VersionsEsParser.BuildSwfFileName(n + 1),
                SwfSha256 = result.RemoteSwfSha256 ?? result.LocalSwfSha256 ?? "",
                VersionsEsSha256 = "",
                VersionsEsRelevantLine = $"maps,es,{result.TargetVersion}",
                SyncedUtc = DateTimeOffset.UtcNow,
                LocalCachePath = result.LocalGeneratedSwfPath,
            };
            AppSettingsStore.Save(_settings);
            RufusLog.Ok($"Publicacion LANG completada · maps_es {result.TargetVersion}");
            MessageBox.Show(this, StatusText.Text, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        else
        {
            RufusLog.Error(result.Error ?? "Publicacion LANG fallida");
            MessageBox.Show(this, StatusText.Text, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatResult(LangRemotePublishResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Success ? "PUBLICACION OK" : "PUBLICACION ERROR");
        sb.AppendLine();
        sb.AppendLine($"SWF subido: {(r.SwfUploaded ? "SI" : "NO")}");
        sb.AppendLine($"versions_es actualizado: {(r.VersionsUpdated ? "SI" : "NO")}");
        sb.AppendLine($"Version activa detectada: {r.ActiveRemoteVersion?.ToString() ?? "—"}");
        sb.AppendLine($"Backup local: {r.LocalBackupPath ?? "—"}");
        sb.AppendLine($"DELETE remoto: {r.DeleteAttemptCount}");
        if (!string.IsNullOrWhiteSpace(r.Error))
        {
            sb.AppendLine();
            sb.AppendLine(r.Error);
        }

        return sb.ToString();
    }
}
