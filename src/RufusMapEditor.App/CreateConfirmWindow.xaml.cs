using System.Windows;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class CreateConfirmWindow : Window
{
    public CreateConfirmWindow(string summary, string databaseLabel)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        SummaryText.Text = summary;
        DatabaseText.Text = "Destino: " + databaseLabel;
    }

    public bool Confirmed { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }
}
