using System.Windows;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class AppearanceWindow : Window
{
    private readonly AppSettings _settings;

    public AppearanceWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ThemeService.ApplyToWindow(this);

        switch (settings.Theme)
        {
            case ThemePreference.Light: ThemeLight.IsChecked = true; break;
            case ThemePreference.Dark: ThemeDark.IsChecked = true; break;
            default: ThemeSystem.IsChecked = true; break;
        }

        ScaleCombo.SelectedIndex = settings.UiScale switch
        {
            <= 0.92 => 0,
            <= 1.02 => 1,
            <= 1.15 => 2,
            _ => 3,
        };
    }

    public ThemePreference SelectedTheme =>
        ThemeLight.IsChecked == true ? ThemePreference.Light :
        ThemeDark.IsChecked == true ? ThemePreference.Dark :
        ThemePreference.System;

    public double SelectedUiScale => ScaleCombo.SelectedIndex switch
    {
        0 => 0.9,
        2 => 1.1,
        3 => 1.25,
        _ => 1.0,
    };

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = SelectedTheme;
        _settings.UiScale = SelectedUiScale;
        AppSettingsStore.Save(_settings);
        ThemeService.SetPreference(SelectedTheme);
        ThemeService.SetUiScale(SelectedUiScale);
        DialogResult = true;
    }
}
