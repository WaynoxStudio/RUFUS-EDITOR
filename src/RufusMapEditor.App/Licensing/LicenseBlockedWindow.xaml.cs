using System.Windows;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App.Licensing;

public partial class LicenseBlockedWindow : Window
{
    private readonly EditorLicenseController? _controller;

    public LicenseBlockedWindow(string message, EditorLicenseController? controller = null)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        MessageText.Text = message;
        _controller = controller ?? App.License;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
        Application.Current.Shutdown();
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null)
        {
            DialogResult = false;
            Close();
            return;
        }

        LicenseRuntimeGate.Clear();
        var ok = await _controller.ReauthorizeAsync(this);
        if (!ok)
            return;

        DialogResult = true;
        Close();
    }
}
