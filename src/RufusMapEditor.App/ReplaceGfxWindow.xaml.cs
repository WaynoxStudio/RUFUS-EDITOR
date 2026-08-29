using System.Windows;

namespace RufusMapEditor.App;

public partial class ReplaceGfxWindow : Window
{
    public int FindId { get; private set; }
    public int ReplaceId { get; private set; }

    public ReplaceGfxWindow(string layerName, int? suggestedReplace)
    {
        InitializeComponent();
        LayerLabel.Text = $"Capa activa: {layerName} (solo selección)";
        if (suggestedReplace is int id)
            ReplaceBox.Text = id.ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(FindBox.Text.Trim(), out var find) ||
            !int.TryParse(ReplaceBox.Text.Trim(), out var replace))
        {
            MessageBox.Show("Introduce GfxID numéricos válidos.", "Reemplazar GFX");
            return;
        }

        FindId = find;
        ReplaceId = replace;
        DialogResult = true;
    }
}
