using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.Services;

/// <summary>LIB.2 / LIB.4.2 — load shared VisualLibrary catalogs from AppSettings (READ-ONLY).</summary>
public static class VisualLibraryBootstrap
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void ConfigurePreviewFromSettings(AppSettings? settings = null)
    {
        settings ??= AppSettingsStore.Load();
        ClipsRootConfiguration.ApplyToRuntime(settings);
    }

    public static async Task EnsureMonstersAsync(AppSettings? settings = null, CancellationToken ct = default)
    {
        settings ??= AppSettingsStore.Load();
        ConfigurePreviewFromSettings(settings);
        if (!string.IsNullOrWhiteSpace(settings.ClipsRootPath))
            VisualLibraryService.Shared.SetClipsRoot(settings.ClipsRootPath);

        if (VisualLibraryService.Shared.MonstersLoaded)
            return;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (VisualLibraryService.Shared.MonstersLoaded)
                return;

            settings.Database ??= new DatabaseSettings();
            settings.LangSftp ??= new LangSftpSettings();
            var dbPassword = DatabasePasswordProtector.Unprotect(settings.Database.PasswordProtectedBase64);
            var langPassword = LangSftpPasswordProtector.Unprotect(settings.LangSftp.PasswordProtectedBase64);

            await VisualLibraryService.Shared.LoadMonstersAsync(
                settings.Database,
                dbPassword,
                langSftp: string.IsNullOrWhiteSpace(settings.LangSftp.Host) ? null : settings.LangSftp,
                langPassword: langPassword,
                clipsRoot: settings.ClipsRootPath,
                ct: ct).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task EnsureItemsAsync(AppSettings? settings = null, CancellationToken ct = default)
    {
        settings ??= AppSettingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.ClipsRootPath))
            VisualLibraryService.Shared.SetClipsRoot(settings.ClipsRootPath);

        if (VisualLibraryService.Shared.ItemsLoaded)
            return;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (VisualLibraryService.Shared.ItemsLoaded)
                return;

            settings.LangSftp ??= new LangSftpSettings();
            var langPassword = LangSftpPasswordProtector.Unprotect(settings.LangSftp.PasswordProtectedBase64);

            await VisualLibraryService.Shared.LoadItemsAsync(
                langSftp: string.IsNullOrWhiteSpace(settings.LangSftp.Host) ? null : settings.LangSftp,
                langPassword: langPassword,
                clipsRoot: settings.ClipsRootPath,
                ct: ct).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
