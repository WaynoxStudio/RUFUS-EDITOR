using System.Windows;

namespace RufusMapEditor.App;

public partial class MapIdInputWindow : Window
{
    public int? ResultMapId { get; private set; }

    public MapIdInputWindow(string prompt, int proposedId)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        IdBox.Text = proposedId.ToString();
        IdBox.SelectAll();
        Loaded += (_, _) => IdBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdBox.Text.Trim(), out var id) || id <= 0)
        {
            MessageBox.Show("Introduce un Map ID numérico válido.", Title);
            return;
        }

        ResultMapId = id;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
