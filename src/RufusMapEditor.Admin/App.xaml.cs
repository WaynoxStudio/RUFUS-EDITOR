using System.IO;
using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.Admin.Services;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.Admin;

public partial class App : Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            var settings = AppSettingsStore.Load();
            ThemeService.Initialize(settings.Theme, settings.UiScale);
            AdminShellTheme.Initialize();
        }
        catch
        {
            ThemeService.Initialize(ThemePreference.Dark, ThemeService.DefaultUiScale);
            AdminShellTheme.Initialize();
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("UI", e.Exception);
        MessageBox.Show(
            e.Exception.Message + "\n\n" + e.Exception.StackTrace,
            "RUFUS ADMIN — error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogCrash("AppDomain", ex);
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RufusMapEditor");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "admin-crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort only.
        }
    }
}
