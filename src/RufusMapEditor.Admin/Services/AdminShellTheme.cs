using System.Windows;
using System.Windows.Media;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.Admin.Services;

/// <summary>
/// Admin-specific shell palette (sidebar, window chrome). Complements ThemeService color packs.
/// </summary>
public static class AdminShellTheme
{
    public static void Initialize()
    {
        Apply(ThemeService.IsDarkEffective);
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private static void OnThemeChanged() => Apply(ThemeService.IsDarkEffective);

    public static void Apply(bool isDark)
    {
        if (Application.Current is null) return;
        var res = Application.Current.Resources;

        if (isDark)
        {
            SetBrush(res, "Bg", "#FF1A1A1A");
            SetBrush(res, "SidebarBg", "#FF1A1A1A");
            SetBrush(res, "ShellText", "#FFF3F3F3");
            SetBrush(res, "ShellMuted", "#FFD0D0D0");
            SetBrush(res, "NavHover", "#FF2E2A24");
            SetBrush(res, "Panel", "#FFE6E0C8");
            SetBrush(res, "PanelElevated", "#FFFFFFFF");
            SetBrush(res, "Text", "#FF2C2416");
            SetBrush(res, "Muted", "#FF5C4F3A");
            SetBrush(res, "SidebarConnectionBackground", "#FF2A241C");
            SetBrush(res, "SidebarConnectionBorder", "#FF3D3528");
            SetBrush(res, "SidebarBorder", "#FF2A2A2A");
            SetBrush(res, "DataGridAltRow", "#FFEDE8D4");
        }
        else
        {
            SetBrush(res, "Bg", "#FFF0EBDC");
            SetBrush(res, "SidebarBg", "#FFF0EBDC");
            SetBrush(res, "ShellText", "#FF2C2416");
            SetBrush(res, "ShellMuted", "#FF5C4F3A");
            SetBrush(res, "NavHover", "#FFD5CCB5");
            SetBrush(res, "Panel", "#FFFAF8F2");
            SetBrush(res, "PanelElevated", "#FFFFFFFF");
            SetBrush(res, "Text", "#FF2C2416");
            SetBrush(res, "Muted", "#FF5C4F3A");
            SetBrush(res, "SidebarConnectionBackground", "#FFE6E0C8");
            SetBrush(res, "SidebarConnectionBorder", "#FFC4BAA0");
            SetBrush(res, "SidebarBorder", "#FFC4BAA0");
            SetBrush(res, "DataGridAltRow", "#FFEDE8D4");
        }
    }

    private static void SetBrush(ResourceDictionary res, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        res[key] = new SolidColorBrush(color);
    }
}
