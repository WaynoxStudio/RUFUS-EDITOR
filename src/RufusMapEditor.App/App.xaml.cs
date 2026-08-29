using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.App.Licensing;
using RufusMapEditor.App.Services;
using RufusMapEditor.Licensing.Options;

namespace RufusMapEditor.App;

public partial class App : Application
{
    public static EditorLicenseController? License { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    private async void App_Startup(object sender, StartupEventArgs e)
    {
        // LIC.7P.1 — bootstrap/licensing must not shutdown when activation dialog closes (OnLastWindowClose).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settings = AppSettingsStore.Load();
        ThemeService.Initialize(settings.Theme, settings.UiScale);

        if (LicenseEnforcementOptions.IsEnforced)
        {
            if (!SingleInstanceGuard.TryAcquire())
            {
                MessageBox.Show(
                    "RUFUS Editor ya está abierto en este equipo.",
                    "RUFUS Map Editor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            Exit += OnAppExitReleaseSingleInstance;

            License = EditorLicenseController.CreateDefault();
            License.SessionInvalidated += OnLicenseSessionInvalidated;
            var ok = await License.EnsureAuthorizedAsync();
            if (!ok)
            {
                Shutdown();
                return;
            }

            License.StartHeartbeat();
        }

        var hub = new StartupHubWindow();
        MainWindow = hub;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Exit += OnAppExitStopHeartbeat;
        hub.Show();
    }

    private void OnLicenseSessionInvalidated(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CloseModuleWindows();

            var blocked = new LicenseBlockedWindow(message, License)
            {
                Owner = MainWindow,
            };
            var reauthorized = blocked.ShowDialog() == true;
            if (!reauthorized)
            {
                Shutdown();
                return;
            }

            License?.StartHeartbeat();
        });
    }

    private static void CloseModuleWindows()
    {
        foreach (Window w in Current.Windows)
        {
            if (w is StartupHubWindow or LicenseBlockedWindow or Licensing.LicenseActivationWindow)
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

    /// <summary>LIC.7P.1 — stop heartbeat on exit; session stays in DPAPI for next launch.</summary>
    private void OnAppExitStopHeartbeat(object? sender, ExitEventArgs e)
    {
        Exit -= OnAppExitStopHeartbeat;
        if (License is null)
            return;

        License.StopHeartbeat();
        License.Dispose();
        License = null;
    }

    private void OnAppExitReleaseSingleInstance(object? sender, ExitEventArgs e)
    {
        Exit -= OnAppExitReleaseSingleInstance;
        SingleInstanceGuard.Release();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var detail = e.Exception.Message;
        if (e.Exception.InnerException is not null)
            detail += "\n\n" + e.Exception.InnerException.Message;

        MessageBox.Show(
            $"Error inesperado:\n{detail}",
            "RUFUS Map Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(
                $"Error fatal:\n{ex.Message}",
                "RUFUS Map Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
