using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App;

public partial class GenerateLangMapsWindow : Window
{
    private readonly int _mapId;
    private int? _sourceVersion;

    public GenerateLangMapsWindow(int mapId, int? worldX, int? worldY, int? subArea)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        _mapId = mapId;
        MapIdBox.Text = mapId.ToString(CultureInfo.InvariantCulture);
        XBox.Text = worldX?.ToString(CultureInfo.InvariantCulture) ?? "0";
        YBox.Text = worldY?.ToString(CultureInfo.InvariantCulture) ?? "0";
        SubAreaBox.Text = subArea?.ToString(CultureInfo.InvariantCulture) ?? "";
        EpBox.Text = "";
        OutputDirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        StatusText.Text = "Seleccione el maps_es de origen y complete EP.";
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seleccionar maps_es de origen",
            Filter = "SWF maps_es|maps_es_*.swf;*.swf|Todos|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true)
            return;

        SourcePathBox.Text = dlg.FileName;
        try
        {
            var info = LangMapsSwfService.Inspect(dlg.FileName);
            _sourceVersion = info.Version;
            SourceVersionBox.Text = info.Version.ToString(CultureInfo.InvariantCulture);
            TargetVersionBox.Text = (info.Version + 1).ToString(CultureInfo.InvariantCulture);
            RefreshTargetFile();
            StatusText.Text = $"Origen OK · VERSION={info.Version} · entradas MA.m={info.EntryCount}";
        }
        catch (Exception ex)
        {
            _sourceVersion = null;
            SourceVersionBox.Text = "";
            TargetVersionBox.Text = "";
            TargetFileBox.Text = "";
            StatusText.Text = "Error al leer origen: " + ex.Message;
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta de salida" };
        if (!string.IsNullOrWhiteSpace(OutputDirBox.Text) && Directory.Exists(OutputDirBox.Text))
            dlg.InitialDirectory = OutputDirBox.Text;
        if (dlg.ShowDialog(this) == true)
        {
            OutputDirBox.Text = dlg.FolderName;
            RefreshTargetFile();
        }
    }

    private void RefreshTargetFile()
    {
        if (_sourceVersion is null || string.IsNullOrWhiteSpace(OutputDirBox.Text))
        {
            TargetFileBox.Text = "";
            return;
        }

        TargetFileBox.Text = Path.Combine(OutputDirBox.Text.Trim(), $"maps_es_{_sourceVersion.Value + 1}.swf");
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourcePathBox.Text) || !File.Exists(SourcePathBox.Text))
        {
            MessageBox.Show(this, "Seleccione un maps_es de origen válido.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
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

        int? ep = null;
        if (!string.IsNullOrWhiteSpace(EpBox.Text)
            && int.TryParse(EpBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epParsed))
            ep = epParsed;

        if (ep is null)
        {
            MessageBox.Show(this, LangMapsSwfService.EpUndefinedMessage, Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var outDir = OutputDirBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outDir))
        {
            MessageBox.Show(this, "Indique carpeta de salida.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusText.Text = "Generando…";
        var result = LangMapsSwfService.Generate(new LangMapsGenerateRequest
        {
            SourceSwfPath = SourcePathBox.Text,
            OutputDirectory = outDir,
            MapId = _mapId,
            X = x,
            Y = y,
            SubArea = sa,
            Ep = ep,
        });

        if (!result.Success)
        {
            StatusText.Text = result.Error ?? "Error";
            MessageBox.Show(this, result.Error ?? "Error", Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusText.Text = $"OK · {(result.Inserted ? "INSERT" : "UPDATE")} · {result.OutputPath}";
        MessageBox.Show(
            this,
            $"SWF generado:\n{result.OutputPath}\n\n" +
            $"VERSION {result.SourceVersion} → {result.TargetVersion}\n" +
            $"MA.m[{_mapId}] {(result.Inserted ? "insertado" : "actualizado")}\n" +
            $"x={x} y={y} sa={sa} ep={ep}\n\n" +
            "Original intacto. Sin publicación remota.",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
