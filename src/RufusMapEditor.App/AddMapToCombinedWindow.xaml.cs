using System.Windows;

namespace RufusMapEditor.App;

public enum CombinedAddChoice
{
    Up,
    Down,
    Left,
    Right,
    Independent,
}

public partial class AddMapToCombinedWindow : Window
{
    public CombinedAddChoice Choice { get; private set; } = CombinedAddChoice.Down;

    public AddMapToCombinedWindow(int mapId, int? anchorMapId, CombinedAddChoice suggested)
    {
        InitializeComponent();
        TitleText.Text = $"Añadir mapa {mapId} al combinado";
        HintText.Text = anchorMapId is int anchor
            ? $"El combinado sigue abierto. Arriba / abajo / izquierda / derecha pegan este mapa a Mapa {anchor}. Independiente lo abre en una ventana aparte, fuera del combinado."
            : "El combinado sigue abierto. Elige si este mapa va pegado al bloque o se abre independiente, en una ventana aparte.";
        ApplySuggested(suggested);
    }

    private void ApplySuggested(CombinedAddChoice suggested)
    {
        RadioUp.IsChecked = suggested == CombinedAddChoice.Up;
        RadioDown.IsChecked = suggested == CombinedAddChoice.Down;
        RadioLeft.IsChecked = suggested == CombinedAddChoice.Left;
        RadioRight.IsChecked = suggested == CombinedAddChoice.Right;
        RadioIndependent.IsChecked = suggested == CombinedAddChoice.Independent;
        if (RadioUp.IsChecked != true &&
            RadioDown.IsChecked != true &&
            RadioLeft.IsChecked != true &&
            RadioRight.IsChecked != true &&
            RadioIndependent.IsChecked != true)
            RadioDown.IsChecked = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (RadioIndependent.IsChecked == true)
            Choice = CombinedAddChoice.Independent;
        else if (RadioUp.IsChecked == true)
            Choice = CombinedAddChoice.Up;
        else if (RadioLeft.IsChecked == true)
            Choice = CombinedAddChoice.Left;
        else if (RadioRight.IsChecked == true)
            Choice = CombinedAddChoice.Right;
        else
            Choice = CombinedAddChoice.Down;
        DialogResult = true;
    }
}
