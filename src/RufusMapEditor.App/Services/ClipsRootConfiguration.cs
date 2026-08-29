using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.Services;

/// <summary>Apply validated ClipsRootPath to shared preview/catalog services (USER + ADMIN).</summary>
public static class ClipsRootConfiguration
{
    public static void ApplyToRuntime(string? clipsRootPath)
    {
        var settings = AppSettingsStore.Load();
        settings.ClipsRootPath = clipsRootPath;
        ApplyToRuntime(settings);
    }

    public static void ApplyToRuntime(AppSettings settings)
    {
        var lib = RufusLibraryPaths.TryResolveEffectiveLibrary(out _)
                  ?? (!string.IsNullOrWhiteSpace(settings.LibraryPath) ? settings.LibraryPath : null);
        var effective = ClipsRootPaths.ResolveEffective(settings.ClipsRootPath);
        ArtworkPreviewService.Shared.Configure(effective, lib);
        NpcGfxPreviewService.Shared.Configure(effective, lib);
        VisualLibraryService.Shared.SetClipsRoot(effective);
    }

    public static bool TrySaveValidatedPath(string? path, out string? normalizedPath, out string errorMessage)
    {
        normalizedPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            normalizedPath = null;
            errorMessage = "";
            return true;
        }

        var validation = ClipsRootPaths.Validate(path);
        if (!validation.IsValid)
        {
            errorMessage = validation.Message;
            return false;
        }

        normalizedPath = validation.NormalizedPath;
        errorMessage = "";
        return true;
    }

    public static void SaveAndApply(AppSettings settings, string? clipsRootPath)
    {
        settings.ClipsRootPath = clipsRootPath;
        AppSettingsStore.Save(settings);
        NpcGfxAppearanceNames.Invalidate();
        ApplyToRuntime(settings);
    }
}
