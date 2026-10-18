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
    private const double ConfigStackBreakpoint = 760;

    private readonly AdminWorkspace _workspace;
    private readonly Window _owner;
    private bool _editingSecret;

    public SettingsView(AdminWorkspace workspace, Window owner)
    {
        InitializeComponent();
        _workspace = workspace;
        _owner = owner;

        _workspace.Changed += Refresh;
        Loaded += (_, _) =>
        {
            BaseUrlBox.Text = _workspace.BaseUrl;
            LoadClipsFields();
            Refresh();
            UpdateConfigLayout(ActualWidth);
        };
        Unloaded += (_, _) => _workspace.Changed -= Refresh;
    }

    private void SettingsRoot_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateConfigLayout(e.NewSize.Width);

    private void UpdateConfigLayout(double width)
    {
        if (width <= 0)
            return;

        if (width < ConfigStackBreakpoint)
        {
            Grid.SetColumn(BackendCard, 0);
            Grid.SetRow(BackendCard, 0);
            Grid.SetColumnSpan(BackendCard, 3);

            Grid.SetColumn(PreviewsCard, 0);
            Grid.SetRow(PreviewsCard, 1);
            Grid.SetColumnSpan(PreviewsCard, 3);

            Grid.SetColumn(ConnectionCard, 0);
            Grid.SetRow(ConnectionCard, 2);
            Grid.SetColumnSpan(ConnectionCard, 3);

            ConfigSpacerColumn.Width = new GridLength(0);
            BackendCard.Margin = new Thickness(0);
            PreviewsCard.Margin = new Thickness(0, 12, 0, 0);
            ConnectionCard.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            Grid.SetColumn(BackendCard, 0);
            Grid.SetRow(BackendCard, 0);
            Grid.SetColumnSpan(BackendCard, 1);

            Grid.SetColumn(PreviewsCard, 2);
            Grid.SetRow(PreviewsCard, 0);
            Grid.SetColumnSpan(PreviewsCard, 1);

            Grid.SetColumn(ConnectionCard, 0);
            Grid.SetRow(ConnectionCard, 1);
            Grid.SetColumnSpan(ConnectionCard, 3);

            ConfigSpacerColumn.Width = new GridLength(12);
            BackendCard.Margin = new Thickness(0);
            PreviewsCard.Margin = new Thickness(0);
            ConnectionCard.Margin = new Thickness(0, 12, 0, 0);
        }
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
                ? "Sin ruta · fallback GFX #ID"
                : $"Autodetectada: {discovered}";
        }
        else
        {
            ClipsStatusText.Text = ClipsRootPaths.Validate(path).Message;
        }

        var settings = AppSettingsStore.Load();
        ClipsRootConfiguration.ApplyToRuntime(settings);
        var cachePaths = PreviewCacheUtility.ResolvePaths(settings.LibraryPath);
        if (cachePaths.LibraryRoot is null)
        {
            PreviewLibraryText.Text = "Library: no resuelta";
            PreviewSpriteCacheText.Text = "Sprite: —";
            PreviewArtworkCacheText.Text = "Artwork: —";
        }
        else
        {
            PreviewLibraryText.Text = "Library: " + cachePaths.LibraryRoot;
            PreviewSpriteCacheText.Text = "Sprite: " + cachePaths.SpriteCacheRoot;
            PreviewArtworkCacheText.Text = "Artwork: " + cachePaths.ArtworkCacheRoot;
        }
    }

    private void ClearPreviewCache_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        var (sprites, artworks) = PreviewCacheUtility.ClearPreviewCaches(settings.LibraryPath);
        RefreshClipsStatus();
        MessageBox.Show(_owner,
            $"Caché limpiada: {sprites} sprite(s), {artworks} artwork(s).",
            "Previews",
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
            MessageBox.Show(_owner, error, "Previews", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClipsRootConfiguration.SaveAndApply(settings, normalized);
        NpcGfxCatalogService.Shared.SetClipsRoot(normalized);
        if (NpcGfxCatalogService.Shared.IsLoaded)
            NpcGfxCatalogService.Shared.ReloadSpriteMetadata(normalized);

        ClipsRootBox.Text = settings.ClipsRootPath ?? "";
        RefreshClipsStatus();
        MessageBox.Show(_owner,
            string.IsNullOrWhiteSpace(normalized) ? "Ruta borrada." : "Carpeta clips guardada.",
            "Previews",
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
                ? "Guardada (DPAPI)."
                : "Sin credencial guardada.";
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
        SecretHintText.Text = "Nuevo secret → Reconectar.";
        ChangeCredentialButton.Content = "Cancelar";
    }
}
