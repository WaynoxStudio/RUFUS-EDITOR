using System.IO;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App.Services;

public sealed class AppSettings
{
    public string? LibraryPath { get; set; }
    public List<GfxFavoriteKey> Favorites { get; set; } = new();
    public List<GfxRecentEntry> Recents { get; set; } = new();
    public List<string> RecentProjects { get; set; } = new();
    /// <summary>Autosave interval in seconds. Default 120.</summary>
    public int AutosaveIntervalSeconds { get; set; } = AutosaveStore.DefaultIntervalSeconds;

    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public double UiScale { get; set; } = ThemeService.DefaultUiScale;
    public MapViewVisibilitySettings MapViewVisibility { get; set; } = new();
    public UiLayoutSettings UiLayout { get; set; } = new();

    /// <summary>MySQL publish settings. Password stored DPAPI-protected only.</summary>
    public DatabaseSettings Database { get; set; } = new();

    /// <summary>LANG / SFTP (READ for LIB.2 catalogs; publish uses write paths separately).</summary>
    public LangSftpSettings LangSftp { get; set; } = new();

    /// <summary>Optional path to retroclient/clips (relative or absolute). No hard-coded Desktop paths.</summary>
    public string? ClipsRootPath { get; set; }
}

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RufusMapEditor");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            if (settings.AutosaveIntervalSeconds < 30)
                settings.AutosaveIntervalSeconds = AutosaveStore.DefaultIntervalSeconds;
            settings.UiLayout ??= new UiLayoutSettings();
            settings.UiLayout.Clamp();
            settings.Database ??= new DatabaseSettings();
            settings.Database.NewMapDefaults ??= new NewMapDefaultsSettings();
            settings.LangSftp ??= new LangSftpSettings();
            if (string.IsNullOrWhiteSpace(settings.LangSftp.LangRemotePath))
                settings.LangSftp.LangRemotePath = LangSftpSettings.DefaultLangRemotePath;
            if (string.IsNullOrWhiteSpace(settings.LangSftp.SwfRemotePath))
                settings.LangSftp.SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath;
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static void TouchRecentProject(AppSettings settings, string path)
    {
        var full = Path.GetFullPath(path);
        settings.RecentProjects.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        settings.RecentProjects.Insert(0, full);
        while (settings.RecentProjects.Count > 12)
            settings.RecentProjects.RemoveAt(settings.RecentProjects.Count - 1);
        Save(settings);
    }
}
