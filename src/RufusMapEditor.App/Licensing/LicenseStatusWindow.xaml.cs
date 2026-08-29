using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.Licensing.Client;

namespace RufusMapEditor.App.Licensing;

public partial class LicenseStatusWindow : Window
{
    private readonly EditorLicenseController? _controller;

    public LicenseStatusWindow(LicenseSessionLocalState? session, string statusLabel, EditorLicenseController? controller = null)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        _controller = controller ?? App.License;
        BindFrom(session, statusLabel, _controller?.StatusFresh ?? true);

        if (_controller is not null)
            _controller.StatusChanged += OnControllerStatusChanged;

        Closed += (_, _) =>
        {
            if (_controller is not null)
                _controller.StatusChanged -= OnControllerStatusChanged;
        };
    }

    private void OnControllerStatusChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnControllerStatusChanged);
            return;
        }

        BindFrom(_controller?.CurrentSession, _controller?.StatusLabel ?? "Licencia: —", _controller?.StatusFresh ?? true);
    }

    private void BindFrom(LicenseSessionLocalState? session, string statusLabel, bool statusFresh)
    {
        if (session is null)
        {
            StatusValue.Text = DeriveEstado(statusLabel);
            ExpiresValue.Text = "—";
            RemainingValue.Text = "—";
            DeviceValue.Text = "—";
            EditorValue.Text = "—";
            AiValue.Text = "—";
            AiTodayValue.Text = "—";
            AiMonthValue.Text = "—";
            ExpiringSoonLine.Visibility = Visibility.Collapsed;
            StaleLine.Visibility = Visibility.Collapsed;
            LogoutButton.IsEnabled = false;
            RefreshButton.IsEnabled = _controller is not null;
            NoteLine.Text =
                "La caducidad la determina el servidor (licenseExpiresAt). " +
                "Se muestra en hora local solo para presentación.";
            return;
        }

        StatusValue.Text = "Activa";
        // licenseExpiresAt from backend → ToLocalTime for display only (not used to authorize).
        ExpiresValue.Text = LicenseStatusDisplay.FormatExpiresLocal(session.LicenseExpiresAt);
        RemainingValue.Text = LicenseStatusDisplay.FormatRemainingDetail(session.LicenseExpiresAt, DateTimeOffset.UtcNow);
        DeviceValue.Text = "Autorizado";
        EditorValue.Text = LicenseStatusDisplay.FormatPermission(session.PermissionEditor);
        AiValue.Text = LicenseStatusDisplay.FormatPermission(session.PermissionAi);
        AiTodayValue.Text = LicenseStatusDisplay.FormatQuota(session.AiUsageToday, session.AiDailyLimit);
        AiMonthValue.Text = LicenseStatusDisplay.FormatQuota(session.AiUsageMonth, session.AiMonthlyLimit);

        ExpiringSoonLine.Visibility = LicenseStatusDisplay.IsExpiringSoon(session.LicenseExpiresAt, DateTimeOffset.UtcNow)
            ? Visibility.Visible
            : Visibility.Collapsed;
        StaleLine.Visibility = statusFresh ? Visibility.Collapsed : Visibility.Visible;

        LogoutButton.IsEnabled = true;
        RefreshButton.IsEnabled = _controller is not null;
        NoteLine.Text =
            "Caducidad = licenseExpiresAt del backend (autoridad servidor), convertida a hora local solo para mostrar. " +
            "El tiempo restante es informativo y no autoriza el Editor. " +
            "Cerrar RUFUS no cierra la sesión; use «Cerrar sesión» para desactivar este equipo.";
    }

    private static string DeriveEstado(string statusLabel)
    {
        if (string.IsNullOrWhiteSpace(statusLabel))
            return "Sin sesión";
        var trimmed = statusLabel.Replace("Licencia: ", "", StringComparison.OrdinalIgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Sin sesión" : trimmed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null)
            return;

        RefreshButton.IsEnabled = false;
        try
        {
            await _controller.RefreshStatusAsync();
            BindFrom(_controller.CurrentSession, _controller.StatusLabel, _controller.StatusFresh);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null)
            return;

        LogoutButton.IsEnabled = false;
        try
        {
            await _controller.LogoutAsync();
            CloseModuleWindows();
            var ok = await _controller.EnsureAuthorizedAsync(Owner);
            if (!ok)
                Application.Current.Shutdown();
            else
            {
                _controller.StartHeartbeat();
                Close();
            }
        }
        finally
        {
            LogoutButton.IsEnabled = true;
        }
    }

    private static void CloseModuleWindows()
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w is StartupHubWindow or LicenseStatusWindow or LicenseActivationWindow or LicenseBlockedWindow)
                continue;
            try
            {
                w.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
