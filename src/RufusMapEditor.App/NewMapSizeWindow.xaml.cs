using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.App;

public partial class NewMapSizeWindow : Window
{
    public int ResultWidth { get; private set; }
    public int ResultHeight { get; private set; }

    private readonly int? _lockWidth;
    private readonly int? _lockHeight;

    public NewMapSizeWindow(int? lockWidth = null, int? lockHeight = null, string? lockReason = null)
    {
        _lockWidth = lockWidth;
        _lockHeight = lockHeight;
        InitializeComponent();
        ThemeService.ApplyToWindow(this);

        if (_lockWidth is int lw && _lockHeight is int lh)
            ApplySizeLock(lw, lh, lockReason);
        else
            ApplyPresetFields();

        Loaded += (_, _) =>
        {
            if (_lockWidth is not null)
                return;
            if (PresetCustom.IsChecked == true)
                WidthBox.Focus();
            else
                PresetMedio.Focus();
        };
    }

    private void ApplySizeLock(int width, int height, string? reason)
    {
        var sizeName = DescribeSize(width, height);
        LockBanner.Visibility = Visibility.Visible;
        LockBannerText.Text =
            reason
            ?? $"Estás trabajando en modo {sizeName} ({width}×{height}). En este combinado solo puedes crear mapas de ese tamaño.";

        IntroText.Text = $"Tamaño fijado al del combinado: {sizeName} ({width}×{height}).";
        FooterHint.Text =
            "Grande / personalizado quedan bloqueados para no romper el mosaico. Al guardar (Ctrl+S) eliges el Map ID.";

        WidthBox.Text = width.ToString();
        HeightBox.Text = height.ToString();
        WidthBox.IsEnabled = false;
        HeightBox.IsEnabled = false;

        PresetMedio.IsChecked = width == BlankMapFactory.MedioWidth && height == BlankMapFactory.MedioHeight;
        PresetGrande.IsChecked = width == BlankMapFactory.GrandeWidth && height == BlankMapFactory.GrandeHeight;
        PresetCustom.IsChecked = PresetMedio.IsChecked != true && PresetGrande.IsChecked != true;

        PresetMedio.IsEnabled = PresetMedio.IsChecked == true;
        PresetGrande.IsEnabled = PresetGrande.IsChecked == true;
        PresetCustom.IsEnabled = PresetCustom.IsChecked == true;

        if (PresetMedio.IsEnabled)
            PresetMedio.ToolTip = null;
        else
            PresetMedio.ToolTip = $"Bloqueado: el combinado es {sizeName} ({width}×{height}).";

        if (PresetGrande.IsEnabled)
            PresetGrande.ToolTip = null;
        else
            PresetGrande.ToolTip = $"Bloqueado: el combinado es {sizeName} ({width}×{height}).";

        if (PresetCustom.IsEnabled)
            PresetCustom.ToolTip = null;
        else
            PresetCustom.ToolTip = $"Bloqueado: el combinado es {sizeName} ({width}×{height}).";
    }

    public static string DescribeSize(int width, int height)
    {
        if (width == BlankMapFactory.MedioWidth && height == BlankMapFactory.MedioHeight)
            return "Medio";
        if (width == BlankMapFactory.GrandeWidth && height == BlankMapFactory.GrandeHeight)
            return "Grande";
        return "Personalizado";
    }

    private void Preset_Changed(object sender, RoutedEventArgs e)
    {
        if (_lockWidth is not null)
            return;
        ApplyPresetFields();
    }

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
        if (_lockWidth is int lw && _lockHeight is int lh)
        {
            ResultWidth = lw;
            ResultHeight = lh;
            DialogResult = true;
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

        ResultWidth = w;
        ResultHeight = h;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
