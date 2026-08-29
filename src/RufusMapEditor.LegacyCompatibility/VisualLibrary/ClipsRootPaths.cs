namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>Validate and optionally discover DOFUS Retro <c>clips</c> folder (sprites.xml).</summary>
public static class ClipsRootPaths
{
    public const string SpritesSubfolder = "sprites";
    public const string SpritesXmlFile = "sprites.xml";

    public sealed class ValidationResult
    {
        public required bool IsValid { get; init; }
        public string? NormalizedPath { get; init; }
        public required string Message { get; init; }
    }

    /// <summary>Configured path if valid; otherwise a single unambiguous discovery hit; else null.</summary>
    public static string? ResolveEffective(string? configuredPath)
    {
        var configured = Validate(configuredPath);
        if (configured.IsValid)
            return configured.NormalizedPath;

        return TryDiscoverUnambiguous();
    }

    public static ValidationResult Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ValidationResult
            {
                IsValid = false,
                Message = "Ruta de clips no configurada.",
            };
        }

        foreach (var candidate in ExpandPathCandidates(path.Trim()))
        {
            var exact = ValidateExact(candidate);
            if (exact.IsValid)
                return exact;
        }

        return new ValidationResult
        {
            IsValid = false,
            Message = "Ruta inválida: debe contener sprites\\sprites.xml (carpeta clips del cliente).",
        };
    }

    public static IReadOnlyList<string> DiscoverValidPaths()
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in BuildDiscoveryCandidates())
        {
            var result = Validate(candidate);
            if (!result.IsValid || result.NormalizedPath is null)
                continue;
            if (seen.Add(result.NormalizedPath))
                found.Add(result.NormalizedPath);
        }

        return found;
    }

    public static string? TryDiscoverUnambiguous()
    {
        var hits = DiscoverValidPaths();
        return hits.Count == 1 ? hits[0] : null;
    }

    public static string? ResolveSpritesXmlPath(string? clipsRoot)
    {
        var effective = ResolveEffective(clipsRoot);
        return effective is null ? null : SpritesXmlParser.ResolveSpritesXmlPath(effective);
    }

    private static ValidationResult ValidateExact(string fullPath)
    {
        if (!Directory.Exists(fullPath))
        {
            return new ValidationResult
            {
                IsValid = false,
                Message = "La carpeta no existe.",
            };
        }

        var spritesDir = Path.Combine(fullPath, SpritesSubfolder);
        var spritesXml = Path.Combine(spritesDir, SpritesXmlFile);
        if (!Directory.Exists(spritesDir))
        {
            return new ValidationResult
            {
                IsValid = false,
                Message = "Falta la subcarpeta sprites\\.",
            };
        }

        if (!File.Exists(spritesXml))
        {
            return new ValidationResult
            {
                IsValid = false,
                Message = "Falta sprites\\sprites.xml en esa ruta.",
            };
        }

        return new ValidationResult
        {
            IsValid = true,
            NormalizedPath = Path.GetFullPath(fullPath),
            Message = "Clips: ✓ sprites.xml encontrado",
        };
    }

    private static IEnumerable<string> ExpandPathCandidates(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            yield break;
        }

        yield return full;

        if (full.EndsWith("retroclient", StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(full, "clips");

        if (full.EndsWith("clips", StringComparison.OrdinalIgnoreCase))
            yield return full;
    }

    private static IEnumerable<string> BuildDiscoveryCandidates()
    {
        var env = Environment.GetEnvironmentVariable("RUFUS_CLIPS_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            yield return env.Trim();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop))
        {
            yield return Path.Combine(desktop, "RUFUS RETRO", "resources", "app", "retroclient", "clips");
            yield return Path.Combine(desktop, "RUFUS RETRO", "resources", "app", "retroclient");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, "Desktop", "RUFUS RETRO", "resources", "app", "retroclient", "clips");
            yield return Path.Combine(userProfile, "Desktop", "RUFUS RETRO", "resources", "app", "retroclient");
        }

        var exeDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(exeDir))
        {
            yield return Path.Combine(exeDir, "clips");
            yield return Path.Combine(exeDir, "retroclient", "clips");
        }
    }
}
