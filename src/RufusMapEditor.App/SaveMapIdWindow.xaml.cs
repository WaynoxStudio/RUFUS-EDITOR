using System.Windows;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class SaveMapIdWindow : Window
{
    public int ResultMapId { get; private set; }

    public SaveMapIdWindow(int proposedMapId, int width, int height)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        PromptText.Text =
            $"Asigna el Map ID para guardar esta plantilla ({width}×{height}).\n" +
            "Puedes usar el sugerido o escribir otro número libre.";
        MapIdBox.Text = proposedMapId > 0 ? proposedMapId.ToString() : "1";
        Loaded += (_, _) =>
        {
            MapIdBox.Focus();
            MapIdBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MapIdBox.Text.Trim(), out var mapId) || mapId <= 0)
        {
            MessageBox.Show(this, "Introduce un Map ID numérico válido (> 0).", Title);
            MapIdBox.Focus();
            return;
        }

        ResultMapId = mapId;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
