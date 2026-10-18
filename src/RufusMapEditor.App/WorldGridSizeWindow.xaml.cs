using System.Windows;

namespace RufusMapEditor.App;

public partial class WorldGridSizeWindow : Window
{
    public int ResultGridWidth { get; private set; } = 10;
    public int ResultGridHeight { get; private set; } = 10;
    public int ResultOriginX { get; private set; }
    public int ResultOriginY { get; private set; }

    public WorldGridSizeWindow(
        int? suggestWidth = null,
        int? suggestHeight = null,
        int? suggestOriginX = null,
        int? suggestOriginY = null)
    {
        InitializeComponent();
        if (suggestWidth is int w && w > 0)
            WidthBox.Text = w.ToString();
        if (suggestHeight is int h && h > 0)
            HeightBox.Text = h.ToString();
        if (suggestOriginX is int ox)
            OriginXBox.Text = ox.ToString();
        if (suggestOriginY is int oy)
            OriginYBox.Text = oy.ToString();
        Loaded += (_, _) => WidthBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(WidthBox.Text.Trim(), out var w) || w < 1 || w > 1000)
        {
            MessageBox.Show("Introduce un ancho válido (1–1000).", Title);
            WidthBox.Focus();
            return;
        }

        if (!int.TryParse(HeightBox.Text.Trim(), out var h) || h < 1 || h > 1000)
        {
            MessageBox.Show("Introduce un alto válido (1–1000).", Title);
            HeightBox.Focus();
            return;
        }

        if (!int.TryParse(OriginXBox.Text.Trim(), out var ox))
        {
            MessageBox.Show("Introduce una coordenada X de inicio válida.", Title);
            OriginXBox.Focus();
            return;
        }

        if (!int.TryParse(OriginYBox.Text.Trim(), out var oy))
        {
            MessageBox.Show("Introduce una coordenada Y de inicio válida.", Title);
            OriginYBox.Focus();
            return;
        }

        ResultGridWidth = w;
        ResultGridHeight = h;
        ResultOriginX = ox;
        ResultOriginY = oy;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
