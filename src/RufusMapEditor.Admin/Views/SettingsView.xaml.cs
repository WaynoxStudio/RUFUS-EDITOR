using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.Admin.Views;

public partial class SettingsView : UserControl
{
    private readonly AdminWorkspace _workspace;
    private readonly Window _owner;
    private bool _editingSecret;
    private bool _syncingThemeToggle;

    public SettingsView(AdminWorkspace workspace, Window owner)
    {
        InitializeComponent();
        _workspace = workspace;
        _owner = owner;
        _workspace.Changed += Refresh;
        ThemeService.ThemeChanged += SyncThemeToggle;
        Loaded += (_, _) =>
        {
            BaseUrlBox.Text = _workspace.BaseUrl;
            LoadClipsFields();
            SyncThemeToggle();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _workspace.Changed -= Refresh;
            ThemeService.ThemeChanged -= SyncThemeToggle;
        };
    }

    private void SyncThemeToggle()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(SyncThemeToggle);
            return;
        }

        _syncingThemeToggle = true;
        ThemeToggle.IsChecked = ThemeService.IsDarkEffective;
        _syncingThemeToggle = false;
    }

    private void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingThemeToggle) return;

        var preference = ThemeToggle.IsChecked == true
            ? ThemePreference.Dark
            : ThemePreference.Light;

        var settings = AppSettingsStore.Load();
        settings.Theme = preference;
        AppSettingsStore.Save(settings);
        ThemeService.SetPreference(preference);
    }

    private void LoadClipsFields()
    {
        var settings = AppSettingsStore.Load();
        ClipsRootBox.Text = settings.ClipsRootPath ?? "";
        RefreshClipsStatus();
    }

    private void RefreshClipsStatus()
    {
        var path = (ClipsRootBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            var discovered = ClipsRootPaths.TryDiscoverUnambiguous();
            ClipsStatusText.Text = discovered is null
                ? "No configurada. Los nombres GFX mostrarán fallback GFX #ID."
                : $"Autodetectada en runtime (no guardada): {discovered}";
        }
        else
        {
            ClipsStatusText.Text = ClipsRootPaths.Validate(path).Message;
        }

        var settings = AppSettingsStore.Load();
        ClipsRootConfiguration.ApplyToRuntime(settings);
        var cachePaths = PreviewCacheUtility.ResolvePaths(settings.LibraryPath);
        PreviewLibraryText.Text = cachePaths.LibraryRoot is null
            ? "Library de previews: no resuelta (cache en runtime según exe/repo)."
            : $"Library efectiva: {cachePaths.LibraryRoot}\n" +
              $"Sprite cache: {cachePaths.SpriteCacheRoot}\n" +
              $"Artwork cache: {cachePaths.ArtworkCacheRoot}";
    }

    private void ClearPreviewCache_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        var (sprites, artworks) = PreviewCacheUtility.ClearPreviewCaches(settings.LibraryPath);
        RefreshClipsStatus();
        MessageBox.Show(_owner,
            $"Caché de previews limpiada.\n\nEliminados: {sprites} PNG sprite(s), {artworks} PNG artwork(s).\n\n" +
            "Las miniaturas se regenerarán al abrir el picker de apariencias.",
            "Carpeta clips",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BrowseClips_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Selecciona la carpeta clips del cliente (…/retroclient/clips)",
        };
        if (!string.IsNullOrWhiteSpace(ClipsRootBox.Text) && Directory.Exists(ClipsRootBox.Text))
            dlg.InitialDirectory = ClipsRootBox.Text;
        if (dlg.ShowDialog() != true)
            return;
        ClipsRootBox.Text = dlg.FolderName;
        RefreshClipsStatus();
    }

    private void SaveClips_Click(object sender, RoutedEventArgs e)
    {
        var path = (ClipsRootBox.Text ?? "").Trim();
        var settings = AppSettingsStore.Load();
        if (!ClipsRootConfiguration.TrySaveValidatedPath(
                string.IsNullOrWhiteSpace(path) ? null : path,
                out var normalized,
                out var error))
        {
            MessageBox.Show(_owner, error, "Carpeta clips", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClipsRootConfiguration.SaveAndApply(settings, normalized);
        NpcGfxCatalogService.Shared.SetClipsRoot(normalized);
        if (NpcGfxCatalogService.Shared.IsLoaded)
            NpcGfxCatalogService.Shared.ReloadSpriteMetadata(normalized);

        ClipsRootBox.Text = settings.ClipsRootPath ?? "";
        RefreshClipsStatus();
        MessageBox.Show(_owner,
            string.IsNullOrWhiteSpace(normalized)
                ? "Ruta de clips borrada."
                : "Carpeta clips guardada. Los pickers NPC usarán nombres de sprites.xml.",
            "Carpeta clips",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Refresh);
            return;
        }

        StateText.Text = _workspace.IsConnected ? "Conectado" : "Sin conexión";
        HostText.Text = _workspace.DisplayHost ?? "—";
        StatusMsgText.Text = _workspace.StatusMessage;
        if (!_editingSecret)
            BaseUrlBox.Text = _workspace.BaseUrl;

        if (!_editingSecret)
        {
            SecretBox.Visibility = Visibility.Collapsed;
            SecretHintText.Text = _workspace.HasSecret
                ? "Credencial guardada (DPAPI). No se muestra en texto plano."
                : "No hay credencial guardada. Pulse Cambiar credencial.";
            ChangeCredentialButton.Content = "Cambiar credencial";
        }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        _workspace.SetBaseUrl(BaseUrlBox.Text);
        if (_editingSecret || !_workspace.HasSecret)
            _workspace.SetSecret(SecretBox.Password);

        var ok = await _workspace.ConnectAndLoadAsync(showErrorDialog: true, _owner);
        if (ok)
        {
            _editingSecret = false;
            SecretBox.Clear();
        }

        Refresh();
    }

    private void ChangeCredential_Click(object sender, RoutedEventArgs e)
    {
        if (_editingSecret)
        {
            _editingSecret = false;
            SecretBox.Clear();
            _workspace.RestoreCredentialsFromStore();
            Refresh();
            return;
        }

        _editingSecret = true;
        _workspace.ClearSecretForChange();
        SecretBox.Visibility = Visibility.Visible;
        SecretBox.Clear();
        SecretBox.Focus();
        SecretHintText.Text = "Introduzca el nuevo secret y pulse Reconectar.";
        ChangeCredentialButton.Content = "Cancelar cambio";
    }
}
