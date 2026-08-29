using System.Windows;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class WorldNameInputWindow : Window
{
    public string? ResultName { get; private set; }

    public WorldNameInputWindow(string prompt, string suggestedName, string geopositionsRoot)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        PromptText.Text = prompt;
        NameBox.Text = string.IsNullOrWhiteSpace(suggestedName) ? "Mundo" : suggestedName.Trim();
        HintText.Text = $"Se guardará en:\n{geopositionsRoot}\\<nombre>\\<nombre>.rufworld";
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = GeopositionsStore.SanitizeProjectName(NameBox.Text);
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "Introduce un nombre para el mundo.", Title);
            return;
        }

        ResultName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
