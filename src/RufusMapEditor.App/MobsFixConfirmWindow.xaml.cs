using System.Windows;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class MobsFixConfirmWindow : Window
{
    public MobsFixConfirmWindow(string previewBody)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        BodyText.Text = previewBody ?? "";
    }

    public bool Confirmed { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
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
