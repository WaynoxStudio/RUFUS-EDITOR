using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App;

public partial class LangRemoteSyncWindow : Window
{
    private readonly LangRemoteSyncResult _result;

    public LangRemoteSyncWindow(LangRemoteSyncResult result)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        _result = result;
        SummaryBox.Text = BuildSummary(result);
        SaveCopyButton.IsEnabled = result.Success
            && !string.IsNullOrWhiteSpace(result.LocalCachePath)
            && File.Exists(result.LocalCachePath!);
    }

    private static string BuildSummary(LangRemoteSyncResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LANG REMOTO");
        sb.AppendLine();
        sb.AppendLine($"Conexión: {(r.ConnectionOk ? "OK" : "ERROR")}");
        sb.AppendLine($"Versión maps activa: {r.MapsVersion?.ToString() ?? "—"}");
        sb.AppendLine($"Archivo: {r.SwfFileName ?? "—"}");
        sb.AppendLine($"VERSION interna: {r.InternalVersion?.ToString() ?? "—"}");
        sb.AppendLine($"Entradas MA.m: {r.MaEntryCount?.ToString() ?? "—"}");
        sb.AppendLine($"Estado: {r.StatusLabel}");
        if (!string.IsNullOrWhiteSpace(r.VersionsEsMapsLine))
            sb.AppendLine($"Token: {r.VersionsEsMapsLine}");
        if (!string.IsNullOrWhiteSpace(r.SwfSha256))
            sb.AppendLine($"SHA-256 SWF: {r.SwfSha256}");
        if (!string.IsNullOrWhiteSpace(r.LocalCachePath))
            sb.AppendLine($"Caché local: {r.LocalCachePath}");
        sb.AppendLine($"Escrituras remotas: {r.RemoteWriteAttempts}");
        if (!r.Success && !string.IsNullOrWhiteSpace(r.Error))
        {
            sb.AppendLine();
            sb.AppendLine("ERROR:");
            sb.AppendLine(r.Error);
        }

        return sb.ToString();
    }

    private void SaveCopy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_result.LocalCachePath) || !File.Exists(_result.LocalCachePath))
        {
            MessageBox.Show(this, "No hay copia en caché para guardar.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Guardar copia local del maps_es",
            FileName = _result.SwfFileName ?? "maps_es.swf",
            Filter = "SWF|*.swf|Todos|*.*",
            AddExtension = true,
            DefaultExt = ".swf",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            File.Copy(_result.LocalCachePath!, dlg.FileName, overwrite: true);
            MessageBox.Show(this, "Copia guardada:\n" + dlg.FileName, Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
