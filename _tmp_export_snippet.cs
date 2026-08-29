using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Rendering.Package;

namespace RufusMapEditor.App.ViewModels;

// Partial file fragment for ExportPackageAsync — applied via script into MainViewModel.cs
internal static class ExportPackageAsyncSnippet
{
    public const string Method = """
    public async Task ExportPackageAsync()
    {
        if (CurrentMap is null || _session is null)
            return;

        if (CurrentMap.Id <= 0)
        {
            MessageBox.Show(
                "MapId inválido. El documento debe tener un MapId > 0 antes de exportar el paquete.",
                "Exportar paquete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!_library.IsLoaded || _library.Renderer is null)
        {
            MessageBox.Show(
                "Configure una biblioteca RUFUS/Astria (necesaria para renderizar PNG/ModeCell).",
                "Exportar paquete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var folderDlg = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta destino del paquete (se creará subcarpeta MapId)",
        };
        if (folderDlg.ShowDialog() != true)
            return;

        var parent = folderDlg.FolderName;
        var packageDir = Path.Combine(parent, CurrentMap.Id.ToString());
        if (Directory.Exists(packageDir))
        {
            var overwrite = MessageBox.Show(
                $"El paquete del mapa {CurrentMap.Id} ya existe:\n{packageDir}\n\nSe actualizarán sus archivos.\n¿Continuar?",
                "Exportar paquete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
                return;
        }

        var cellIds = MessageBox.Show(
            "¿Incluir IDs de celda en el PNG ModeCell?\n\n(Recomendado: Sí — imagen técnica determinista)",
            "Exportar paquete — ModeCell",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        var dirtyBefore = IsDirty;

        StatusText = "Exportando paquete...";
        IsLoading = true;
        MapPackageResult result;
        try
        {
            var map = CurrentMap;
            var renderer = _library.Renderer;
            var libraryRoot = _library.RootPath;
            var documentId = _session.DocumentId;
            var source = _session.Source;
            var projectName = _session.ProjectName;
            result = await Task.Run(() =>
            {
                var builder = new MapPackageBuilder(renderer);
                return builder.Build(map, new MapPackageOptions
                {
                    ParentDirectory = parent,
                    DocumentId = documentId,
                    Source = source,
                    ProjectName = projectName,
                    ShowCellIds = cellIds,
                    LibraryRootForSwf = libraryRoot,
                });
            });
        }
        finally
        {
            IsLoading = false;
        }

        // Export must not alter dirty state
        _ = dirtyBefore;
        AfterHistoryChange();

        if (!result.Success)
        {
            MessageBox.Show(result.ErrorMessage ?? "Error al exportar paquete", "Exportar paquete",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Exportación de paquete fallida";
            return;
        }

        var files = string.Join("\n", result.CoreFiles.Select(f => "  • " + f));
        var swfLine = result.LegacySwfGenerated
            ? $"  • Legacy\\{result.MapId}_AME.swf"
            : $"  • Legacy SWF: NO GENERADO\n    ({result.LegacySwfWarning})";

        var hashPreview = result.MapDataSha256.Length >= 16
            ? result.MapDataSha256[..16]
            : result.MapDataSha256;

        var summary =
            $"Paquete generado correctamente.\n\n" +
            $"Map ID: {result.MapId}\n" +
            $"Ruta:\n{result.PackageDirectory}\n\n" +
            $"Archivos:\n{files}\n{swfLine}\n\n" +
            $"PNG: {result.PngWidth}×{result.PngHeight}\n" +
            $"ModeCell: {result.ModeCellWidth}×{result.ModeCellHeight}\n" +
            $"MapData SHA256: {hashPreview}…";

        var open = MessageBox.Show(
            summary + "\n\n¿Abrir carpeta?",
            "Exportar paquete",
            MessageBoxButton.YesNo,
            result.LegacySwfGenerated ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (open == MessageBoxResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = result.PackageDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la carpeta:\n{ex.Message}", "Exportar paquete");
            }
        }

        StatusText = result.LegacySwfGenerated
            ? "Paquete exportado"
            : "Paquete exportado (SWF AME omitido)";
    }

""";
}
