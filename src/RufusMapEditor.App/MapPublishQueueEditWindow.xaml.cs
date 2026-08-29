using System.Globalization;
using System.Windows;

namespace RufusMapEditor.App;

public partial class MapPublishQueueEditWindow : Window
{
    public int MapId { get; }
    public int SubArea { get; private set; }
    public int Ep { get; private set; }
    public int WorldX { get; private set; }
    public int WorldY { get; private set; }

    public string MapIdLine { get; }

    public MapPublishQueueEditWindow(int mapId, int? x, int? y, int? subArea, int ep)
    {
        InitializeComponent();
        Services.ThemeService.ApplyToWindow(this);
        MapId = mapId;
        MapIdLine = $"Map ID: {mapId}";
        DataContext = this;
        if (x is int wx) XBox.Text = wx.ToString(CultureInfo.InvariantCulture);
        if (y is int wy) YBox.Text = wy.ToString(CultureInfo.InvariantCulture);
        if (subArea is int sa) SubAreaBox.Text = sa.ToString(CultureInfo.InvariantCulture);
        EpBox.Text = ep.ToString(CultureInfo.InvariantCulture);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(XBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(YBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            MessageBox.Show(this, "X e Y deben ser enteros (0 es válido).", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(SubAreaBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sa))
        {
            MessageBox.Show(this, "SubArea (sa) debe ser un entero.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(EpBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ep) || ep < 0)
        {
            MessageBox.Show(this, "EP debe ser un entero ≥ 0 (predeterminado 2).", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WorldX = x;
        WorldY = y;
        SubArea = sa;
        Ep = ep;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
