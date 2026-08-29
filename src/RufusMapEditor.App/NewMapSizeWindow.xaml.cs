using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.App;

public partial class NewMapSizeWindow : Window
{
    public int ResultWidth { get; private set; }
    public int ResultHeight { get; private set; }

    public NewMapSizeWindow()
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        ApplyPresetFields();
        Loaded += (_, _) =>
        {
            if (PresetCustom.IsChecked == true)
                WidthBox.Focus();
            else
                PresetMedio.Focus();
        };
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

        ResultWidth = w;
        ResultHeight = h;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
