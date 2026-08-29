using System.Windows;

namespace RufusMapEditor.App;

public partial class MapBatchPublishConfirmWindow : Window
{
    public bool Confirmed { get; private set; }

    public MapBatchPublishConfirmWindow(string preview, int mapCount)
    {
        InitializeComponent();
        Services.ThemeService.ApplyToWindow(this);
        PreviewBox.Text = preview;
        PublishButton.Content = mapCount == 1 ? "PUBLICAR 1 MAPA" : $"PUBLICAR {mapCount} MAPAS";
    }

    private void Publish_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}
