using System.Windows;

namespace RufusMapEditor.App;

public partial class WorldCoordInputWindow : Window
{
    public int? ResultX { get; private set; }
    public int? ResultY { get; private set; }

    public WorldCoordInputWindow(string prompt, int currentX, int currentY)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        XBox.Text = currentX.ToString();
        YBox.Text = currentY.ToString();
        Loaded += (_, _) =>
        {
            XBox.Focus();
            XBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(XBox.Text.Trim(), out var x))
        {
            MessageBox.Show("Introduce una coordenada X numérica válida.", Title);
            XBox.Focus();
            XBox.SelectAll();
            return;
        }

        if (!int.TryParse(YBox.Text.Trim(), out var y))
        {
            MessageBox.Show("Introduce una coordenada Y numérica válida.", Title);
            YBox.Focus();
            YBox.SelectAll();
            return;
        }

        ResultX = x;
        ResultY = y;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
