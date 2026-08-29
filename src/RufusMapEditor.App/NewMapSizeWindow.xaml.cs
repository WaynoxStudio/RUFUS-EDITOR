using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.App;

public partial class NewMapSizeWindow : Window
{
    public int ResultMapId { get; private set; }
    public int ResultWidth { get; private set; }
    public int ResultHeight { get; private set; }

    public NewMapSizeWindow(int proposedMapId)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        MapIdBox.Text = proposedMapId > 0 ? proposedMapId.ToString() : "1";
        ApplyPresetFields();
        Loaded += (_, _) => MapIdBox.Focus();
    }

    private void Preset_Changed(object sender, RoutedEventArgs e) => ApplyPresetFields();

    private void ApplyPresetFields()
    {
        if (WidthBox is null || HeightBox is null) return;

        if (PresetMedio?.IsChecked == true)
        {
            WidthBox.Text = BlankMapFactory.MedioWidth.ToString();
            HeightBox.Text = BlankMapFactory.MedioHeight.ToString();
            WidthBox.IsEnabled = false;
            HeightBox.IsEnabled = false;
        }
        else if (PresetGrande?.IsChecked == true)
        {
            WidthBox.Text = BlankMapFactory.GrandeWidth.ToString();
            HeightBox.Text = BlankMapFactory.GrandeHeight.ToString();
            WidthBox.IsEnabled = false;
            HeightBox.IsEnabled = false;
        }
        else
        {
            WidthBox.IsEnabled = true;
            HeightBox.IsEnabled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MapIdBox.Text.Trim(), out var mapId) || mapId <= 0)
        {
            MessageBox.Show(this, "Introduce un Map ID numérico válido (> 0).", Title);
            MapIdBox.Focus();
            return;
        }

        if (!int.TryParse(WidthBox.Text.Trim(), out var w) || w < 1 || w > 100)
        {
            MessageBox.Show(this, "Ancho inválido (1–100).", Title);
            WidthBox.Focus();
            return;
        }

        if (!int.TryParse(HeightBox.Text.Trim(), out var h) || h < 1 || h > 100)
        {
            MessageBox.Show(this, "Alto inválido (1–100).", Title);
            HeightBox.Focus();
            return;
        }

        try
        {
            _ = MapGeometry.CellCount(w, h);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Tamaño no válido:\n{ex.Message}", Title);
            return;
        }

        ResultMapId = mapId;
        ResultWidth = w;
        ResultHeight = h;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
