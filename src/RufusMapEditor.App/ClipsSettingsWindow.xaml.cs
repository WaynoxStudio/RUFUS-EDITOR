using System.IO;
using System.Windows;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App;

public partial class ClipsSettingsWindow : Window
{
    private readonly AppSettings _settings;

    public ClipsSettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        _settings = settings;
        PathBox.Text = settings.ClipsRootPath ?? "";
        RefreshStatus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Selecciona la carpeta clips del cliente (…/retroclient/clips)",
        };
        if (!string.IsNullOrWhiteSpace(PathBox.Text) && Directory.Exists(PathBox.Text))
            dlg.InitialDirectory = PathBox.Text;
        if (dlg.ShowDialog(this) != true)
            return;
        PathBox.Text = dlg.FolderName;
        RefreshStatus();
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        var (spr, art) = PreviewCacheUtility.ClearPreviewCaches(settings.LibraryPath);
        MessageBox.Show(this,
            $"Caché de previews limpiada.\n\nEliminados: {spr} PNG sprite(s), {art} PNG artwork(s).",
            "Clips / Previews",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = (PathBox.Text ?? "").Trim();
        if (!ClipsRootConfiguration.TrySaveValidatedPath(
                string.IsNullOrWhiteSpace(path) ? null : path,
                out var normalized,
                out var error))
        {
            MessageBox.Show(this, error, "Carpeta clips", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClipsRootConfiguration.SaveAndApply(_settings, normalized);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RefreshStatus()
    {
        var path = (PathBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            var discovered = ClipsRootPaths.TryDiscoverUnambiguous();
            StatusText.Text = discovered is null
                ? "⚠ Ruta de clips no configurada"
                : $"Autodetectado (no guardado): {discovered}";
            return;
        }

        StatusText.Text = ClipsRootPaths.Validate(path).Message;
    }
}
