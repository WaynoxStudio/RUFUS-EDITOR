using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Central theme application: semantic brushes + shared control styles.
/// Hot-swaps merged dictionaries on Application.Current.
/// </summary>
public static class ThemeService
{
    public const double DefaultUiScale = 1.0;

    // Pack URIs resolve against assembly RufusMapEditor (AssemblyName) — works for USER and ADMIN host.
    private static readonly Uri DarkColorsUri =
        new("pack://application:,,,/RufusMapEditor;component/Themes/ColorsDark.xaml");
    private static readonly Uri LightColorsUri =
        new("pack://application:,,,/RufusMapEditor;component/Themes/ColorsLight.xaml");
    private static readonly Uri ControlsUri =
        new("pack://application:,,,/RufusMapEditor;component/Themes/Controls.xaml");
    private static readonly Uri ToolIconsUri =
        new("pack://application:,,,/RufusMapEditor;component/Themes/ToolIcons.xaml");


    public static event Action? ThemeChanged;

    public static ThemePreference CurrentPreference { get; private set; } = ThemePreference.System;
    public static bool IsDarkEffective { get; private set; } = true;
    public static double UiScale { get; private set; } = DefaultUiScale;

    public static void Initialize(ThemePreference preference, double uiScale = DefaultUiScale)
    {
        CurrentPreference = preference;
        UiScale = ClampScale(uiScale);
        ApplyEffectiveTheme(ResolveEffectiveTheme(preference));
    }

    public static void SetPreference(ThemePreference preference)
    {
        if (preference == CurrentPreference) return;
        CurrentPreference = preference;
        ApplyEffectiveTheme(ResolveEffectiveTheme(preference));
    }

    public static void SetUiScale(double scale)
    {
        scale = ClampScale(scale);
        if (Math.Abs(scale - UiScale) < 0.001) return;
        UiScale = scale;
        ApplyEffectiveTheme(IsDarkEffective ? ThemePreference.Dark : ThemePreference.Light, refreshOnlyScale: true);
    }

    public static ThemePreference ResolveEffectiveTheme(ThemePreference preference) =>
        preference == ThemePreference.System
            ? (IsWindowsAppsLightTheme() ? ThemePreference.Light : ThemePreference.Dark)
            : preference;

    private static bool IsWindowsAppsLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int i && i != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyEffectiveTheme(ThemePreference effective, bool refreshOnlyScale = false)
    {
        IsDarkEffective = effective == ThemePreference.Dark;
        if (Application.Current is null) return;

        var appResources = Application.Current.Resources;
        var merged = appResources.MergedDictionaries;

        if (!refreshOnlyScale)
        {
            // Do not wipe host dictionaries (ADMIN shell styles). Replace only Maps theme packs.
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.OriginalString ?? "";
                if (src.Contains("ColorsDark.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.Contains("ColorsLight.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.Contains("Themes/Controls.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.Contains("ToolIcons.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.EndsWith("/Controls.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.EndsWith("\\Controls.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    merged.RemoveAt(i);
                }
            }

            merged.Insert(0, LoadDictionary(effective == ThemePreference.Dark ? DarkColorsUri : LightColorsUri));
            merged.Insert(1, LoadDictionary(ControlsUri));
            merged.Insert(2, LoadDictionary(ToolIconsUri));
        }

        appResources["UiScale"] = UiScale;
        appResources["FontSizeSmall"] = 11 * UiScale;
        appResources["FontSizeNormal"] = 13 * UiScale;
        appResources["FontSizeSection"] = 14 * UiScale;
        appResources["FontSizeTitle"] = 16 * UiScale;

        ThemeChanged?.Invoke();
    }

    private static ResourceDictionary LoadDictionary(Uri uri) =>
        new() { Source = uri };

    private static double ClampScale(double scale) => Math.Clamp(scale, 0.85, 1.35);

    public static void ApplyToWindow(Window window)
    {
        if (window is null) return;
        window.Background = GetBrush("WindowBackground");
        window.Foreground = GetBrush("TextPrimary");
    }

    public static SolidColorBrush GetBrush(string key)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush(Colors.Magenta);
    }
}
