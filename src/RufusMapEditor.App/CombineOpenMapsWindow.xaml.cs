using System.Windows;

namespace RufusMapEditor.App;

public partial class CombineOpenMapsWindow : Window
{
    public bool Horizontal { get; private set; } = true;

    public CombineOpenMapsWindow(int mapCount)
    {
        InitializeComponent();
        HintText.Text =
            $"Se van a pegar {mapCount} mapa(s) abiertos en la pestaña MAPA (sin huecos). " +
            "Cada mapa sigue siendo independiente al guardar. " +
            "Cuando quieras, puedes enviar este combinado a la pestaña MUNDO.";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Horizontal = RadioHorizontal.IsChecked == true;
        DialogResult = true;
    }
}
