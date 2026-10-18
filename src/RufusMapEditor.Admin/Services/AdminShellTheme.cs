using System.Windows;
using System.Windows.Media;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.Admin.Services;

/// <summary>
/// Admin-specific shell palette (sidebar, window chrome, content surfaces).
/// Complements ThemeService color packs; overrides shared semantic keys so embedded
/// Maps/Content modules never show the editor's dark WindowBackground in light mode.
/// </summary>
public static class AdminShellTheme
{
    private static readonly string[] SharedOverrideKeys =
    [
        "WindowBackground",
        "CanvasBackground",
        "BrandBarBackground",
        "PanelBackground",
        "SurfaceBackground",
        "ElevatedSurface",
        "InspectorCardBackground",
        "PanelCardBackground",
        "TextPrimary",
        "TextSecondary",
        "TextMuted",
        "InputBackground",
        "InputForeground",
        "InputBorder",
        "HoverBackground",
        "SecondaryButtonBackground",
        "Border",
        "Divider",
        "DialogHeaderBackground",
        "HeaderForeground",
        "HeaderOnBrownSubtle",
        "PreviewEmpty",
        "PreviewEmptyBorder",
        "CheckerA",
        "CheckerB",
        "ThumbBackground",
        "LogConsoleBackground",
    ];

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

        ClearSharedOverrides(res);

        if (isDark)
        {
            ApplyAdminChrome(res, isDark: true);
            ApplySharedContentPalette(res, isDark: true);
            SetSystemMenuColors(res, menuBg: "#FF38342C", menuText: "#FFF5F0E6", highlight: "#FFF39C12", highlightText: "#FFFFFFFF");
        }
        else
        {
            ClearSystemMenuColors(res);
            ApplyAdminChrome(res, isDark: false);
            ApplySharedContentPalette(res, isDark: false);
        }
    }

    private static void ApplyAdminChrome(ResourceDictionary res, bool isDark)
    {
        if (isDark)
        {
            SetBrush(res, "Bg", "#FF1A1A1A");
            SetBrush(res, "SidebarBg", "#FF1A1A1A");
            SetBrush(res, "ShellText", "#FFF3F3F3");
            SetBrush(res, "ShellMuted", "#FFB0A898");
            SetBrush(res, "NavHover", "#FF3A3530");
            SetBrush(res, "Panel", "#FF2C2820");
            SetBrush(res, "PanelElevated", "#FF38342C");
            SetBrush(res, "Text", "#FFF5F0E6");
            SetBrush(res, "Muted", "#FFB8AA96");
            SetBrush(res, "Danger", "#FFE57373");
            SetBrush(res, "SidebarConnectionBackground", "#FF252220");
            SetBrush(res, "SidebarConnectionBorder", "#FF3D3830");
            SetBrush(res, "SidebarBorder", "#FF333333");
            SetBrush(res, "DataGridAltRow", "#FF322E26");
        }
        else
        {
            SetBrush(res, "Bg", "#FFF0EBDC");
            SetBrush(res, "SidebarBg", "#FFF0EBDC");
            SetBrush(res, "ShellText", "#FF1F1A12");
            SetBrush(res, "ShellMuted", "#FF3D3428");
            SetBrush(res, "NavHover", "#FFD5CCB5");
            SetBrush(res, "Panel", "#FFF7F3E8");
            SetBrush(res, "PanelElevated", "#FFFFFCF7");
            SetBrush(res, "Text", "#FF1F1A12");
            SetBrush(res, "Muted", "#FF3D3428");
            SetBrush(res, "Danger", "#FFC0392B");
            SetBrush(res, "SidebarConnectionBackground", "#FFE6E0C8");
            SetBrush(res, "SidebarConnectionBorder", "#FFC4BAA0");
            SetBrush(res, "SidebarBorder", "#FFC4BAA0");
            SetBrush(res, "DataGridAltRow", "#FFEDE8D4");
        }
    }

    /// <summary>
    /// Shared keys used by MapsEditorView / ContentWorkspaceView and Controls.xaml.
    /// ColorsLight keeps WindowBackground dark for standalone Map Editor chrome; Admin overrides it.
    /// </summary>
    private static void ApplySharedContentPalette(ResourceDictionary res, bool isDark)
    {
        if (isDark)
        {
            SetBrush(res, "WindowBackground", "#FF2C2820");
            SetBrush(res, "CanvasBackground", "#FF1E1B18");
            SetBrush(res, "BrandBarBackground", "#FF1A1A1A");
            SetBrush(res, "PanelBackground", "#FF2C2820");
            SetBrush(res, "SurfaceBackground", "#FF2C2820");
            SetBrush(res, "ElevatedSurface", "#FF38342C");
            SetBrush(res, "InspectorCardBackground", "#FF2C2820");
            SetBrush(res, "PanelCardBackground", "#FF2C2820");
            SetBrush(res, "TextPrimary", "#FFF7F2E8");
            SetBrush(res, "TextSecondary", "#FFE4D8C4");
            SetBrush(res, "TextMuted", "#FFCFC0A8");
            SetBrush(res, "InputBackground", "#FF3A3530");
            SetBrush(res, "InputForeground", "#FFF7F2E8");
            SetBrush(res, "InputBorder", "#FF5C5548");
            SetBrush(res, "HoverBackground", "#FF3A3530");
            SetBrush(res, "SecondaryButtonBackground", "#FF4A4438");
            SetBrush(res, "Border", "#FF5C5548");
            SetBrush(res, "Divider", "#FF4A4438");
            SetBrush(res, "DialogHeaderBackground", "#FF6B5340");
            SetBrush(res, "HeaderForeground", "#FFFFFFFF");
            SetBrush(res, "HeaderOnBrownSubtle", "#FFF5F0E6");
            SetBrush(res, "PreviewEmpty", "#FF38342C");
            SetBrush(res, "PreviewEmptyBorder", "#FF5C5548");
            SetBrush(res, "CheckerA", "#FF2C2820");
            SetBrush(res, "CheckerB", "#FF322E26");
            SetBrush(res, "ThumbBackground", "#FF38342C");
            SetBrush(res, "LogConsoleBackground", "#FF1A1A1A");
        }
        else
        {
            SetBrush(res, "WindowBackground", "#FFF7F3E8");
            SetBrush(res, "CanvasBackground", "#FFE8E8E8");
            SetBrush(res, "BrandBarBackground", "#FFF0EBDC");
            SetBrush(res, "PanelBackground", "#FFF7F3E8");
            SetBrush(res, "SurfaceBackground", "#FFFFFCF7");
            SetBrush(res, "ElevatedSurface", "#FFFFFCF7");
            SetBrush(res, "InspectorCardBackground", "#FFF3EEE0");
            SetBrush(res, "PanelCardBackground", "#FFFFFCF7");
            SetBrush(res, "TextPrimary", "#FF1F1A12");
            SetBrush(res, "TextSecondary", "#FF3D3428");
            SetBrush(res, "TextMuted", "#FF5C4F3A");
            SetBrush(res, "InputBackground", "#FFFFFFFF");
            SetBrush(res, "InputForeground", "#FF1F1A12");
            SetBrush(res, "InputBorder", "#FFC4BAA0");
            SetBrush(res, "HoverBackground", "#FFE8DFC8");
            SetBrush(res, "SecondaryButtonBackground", "#FFE8DFC8");
            SetBrush(res, "Border", "#FFC4BAA0");
            SetBrush(res, "Divider", "#FFD5CCB5");
            SetBrush(res, "DialogHeaderBackground", "#FF8B7355");
            SetBrush(res, "HeaderForeground", "#FFFFFFFF");
            SetBrush(res, "HeaderOnBrownSubtle", "#FFF5F0E6");
            SetBrush(res, "PreviewEmpty", "#FFEDE8D4");
            SetBrush(res, "PreviewEmptyBorder", "#FFC4BAA0");
            SetBrush(res, "CheckerA", "#FFE6E0C8");
            SetBrush(res, "CheckerB", "#FFEDE8D4");
            SetBrush(res, "ThumbBackground", "#FFEDE8D4");
            SetBrush(res, "LogConsoleBackground", "#FF2C2416");
        }
    }

    private static void ClearSharedOverrides(ResourceDictionary res)
    {
        foreach (var key in SharedOverrideKeys)
            res.Remove(key);
    }

    private static void SetSystemMenuColors(
        ResourceDictionary res,
        string menuBg,
        string menuText,
        string highlight,
        string highlightText)
    {
        SetBrush(res, SystemColors.MenuBrushKey, menuBg);
        SetBrush(res, SystemColors.MenuTextBrushKey, menuText);
        SetBrush(res, SystemColors.HighlightBrushKey, highlight);
        SetBrush(res, SystemColors.HighlightTextBrushKey, highlightText);
        SetBrush(res, SystemColors.WindowBrushKey, menuBg);
        SetBrush(res, SystemColors.WindowTextBrushKey, menuText);
    }

    private static void ClearSystemMenuColors(ResourceDictionary res)
    {
        res.Remove(SystemColors.MenuBrushKey);
        res.Remove(SystemColors.MenuTextBrushKey);
        res.Remove(SystemColors.HighlightBrushKey);
        res.Remove(SystemColors.HighlightTextBrushKey);
        res.Remove(SystemColors.WindowBrushKey);
        res.Remove(SystemColors.WindowTextBrushKey);
    }

    private static void SetBrush(ResourceDictionary res, ResourceKey key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        res[key] = new SolidColorBrush(color);
    }

    private static void SetBrush(ResourceDictionary res, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        res[key] = new SolidColorBrush(color);
    }
}
