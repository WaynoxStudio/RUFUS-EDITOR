using System.Windows;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class RevisionInputWindow : Window
{
    public string? ResultRevision { get; private set; }

    public RevisionInputWindow(string currentFecha)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        PromptText.Text =
            $"El valor de revisión actual no es numérico: {currentFecha}\n\n" +
            "Indique una revisión nueva válida (entero) antes de publicar.";
        Loaded += (_, _) =>
        {
            RevisionBox.Focus();
            RevisionBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var t = RevisionBox.Text.Trim();
        if (!int.TryParse(t, out _))
        {
            MessageBox.Show(this, "La revisión debe ser un entero válido.", Title);
            return;
        }

        ResultRevision = t;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
