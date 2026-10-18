using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    private void App_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settings = AppSettingsStore.Load();
        ThemeService.Initialize(settings.Theme, settings.UiScale);

#if RUFUS_USER
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
#endif

        var hub = new StartupHubWindow();
        MainWindow = hub;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        hub.Show();
    }

#if RUFUS_USER
    private void OnAppExitReleaseSingleInstance(object? sender, ExitEventArgs e)
    {
        Exit -= OnAppExitReleaseSingleInstance;
        SingleInstanceGuard.Release();
    }
#endif

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
