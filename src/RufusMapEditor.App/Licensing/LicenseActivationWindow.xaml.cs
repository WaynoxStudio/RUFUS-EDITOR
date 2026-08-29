using System.Net.Http;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Options;

namespace RufusMapEditor.App.Licensing;

public partial class LicenseActivationWindow : Window
{
    private readonly EditorLicenseSessionService _service;
    private bool _busy;

    public LicenseSessionLocalState? AuthorizedSession { get; private set; }

    public LicenseActivationWindow(EditorLicenseSessionService service, string? initialError = null)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        _service = service;
#if RUFUS_USER
        ServiceStatusButton.Visibility = Visibility.Collapsed;
#endif
        if (!string.IsNullOrWhiteSpace(initialError))
            ShowError(initialError);
        Loaded += (_, _) => CodeBox.Focus();
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var code = (CodeBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowError("Introduce un código de licencia.");
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _service.ActivateAsync(code);
            CodeBox.Clear();
            if (result.Outcome == LicenseGateOutcome.Authorized && result.Session is not null)
            {
                AuthorizedSession = result.Session;
                DialogResult = true;
                return;
            }

            ShowError(string.IsNullOrWhiteSpace(result.UserMessage)
                ? LicenseUserMessages.Generic
                : result.UserMessage);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ServiceStatus_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var baseUrl = (Environment.GetEnvironmentVariable(HttpLicenseClient.BaseUrlEnvironmentVariable)
                           ?? LicenseApiDefaults.ProductionBaseUrl).Trim().TrimEnd('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var resp = await http.GetAsync(baseUrl + "/health");
            var ok = resp.IsSuccessStatusCode;
            MessageBox.Show(
                this,
                ok
                    ? "Servicio de licencias: disponible."
                    : "Servicio de licencias: no disponible en este momento.",
                "Estado del servicio",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch
        {
            MessageBox.Show(
                this,
                LicenseUserMessages.ServiceUnavailable,
                "Estado del servicio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ActivateButton.IsEnabled = !busy;
        CodeBox.IsEnabled = !busy;
        BusyText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
}
