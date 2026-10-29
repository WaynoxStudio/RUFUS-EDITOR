using System.Windows;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class CreateConfirmWindow : Window
{
    public CreateConfirmWindow(string summary, string databaseLabel, string? initialFecha = null)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        SummaryText.Text = summary;
        DatabaseText.Text = "Destino: " + databaseLabel;
        FechaBox.Text = string.IsNullOrWhiteSpace(initialFecha) ? "0" : initialFecha.Trim();
        Loaded += (_, _) =>
        {
            FechaBox.Focus();
            FechaBox.SelectAll();
        };
    }

    public bool Confirmed { get; private set; }

    public string ChosenFecha => FechaBox.Text ?? "";

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }
}
