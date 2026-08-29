using System.Text.Json;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// Resolves Editor BackendUrl (AI.4A/AI.4C). Never stores OpenAI keys.
/// AI.6B.2 — temporary private VPS HTTPS endpoint (no local AiBackend on the PC).
/// </summary>
public static class AiBackendLocalDevUrl
{
    public const string GenerateRelativePath = "/v1/ai/generate";
    public const string BackendUrlEnvironmentVariable = "RUFUS_AI_BACKEND_URL";

    /// <summary>AI.6B.2 — temporary VPS generate URL over HTTPS (no HTTP fallback).</summary>
    public const string TemporaryVpsGenerateEndpoint =
        "https://vmi3502135.contaboserver.net/v1/ai/generate";

    public const string TemporaryVpsSource = "AI.6B.2-temporary-vps-https";

    /// <summary>
    /// Resolves the absolute generate endpoint for the editor.
    /// Priority: RUFUS_AI_BACKEND_URL → temporary VPS HTTPS (AI.6B.2).
    /// Does not fall back to HTTP or local 127.0.0.1:5088 during this phase.
    /// </summary>
    public static bool TryResolveGenerateEndpoint(out string endpointUrl, out string source)
    {
        endpointUrl = "";
        source = "";

        var env = Environment.GetEnvironmentVariable(BackendUrlEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (!TryNormalizeEndpoint(env.Trim(), out endpointUrl))
                return false;
            source = BackendUrlEnvironmentVariable;
            return true;
        }

        // AI.6B.2 — private VPS HTTPS (Editor must not require local RufusMapEditor.AiBackend).
        if (TryNormalizeEndpoint(TemporaryVpsGenerateEndpoint, out endpointUrl))
        {
            source = TemporaryVpsSource;
            return true;
        }

        return false;
    }

    public static bool TryNormalizeEndpoint(string urlOrBase, out string endpointUrl)
    {
        endpointUrl = "";
        if (string.IsNullOrWhiteSpace(urlOrBase))
            return false;

        var candidates = urlOrBase
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Prefer http for local launchSettings (often lists http;https).
        var raw = candidates.FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                  ?? candidates.FirstOrDefault();
        if (raw is null)
            return false;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1/ai/generate", StringComparison.OrdinalIgnoreCase))
        {
            endpointUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return true;
        }

        endpointUrl = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + GenerateRelativePath;
        return true;
    }

    public static bool TryFindLaunchSettingsPath(out string path)
    {
        path = "";
        foreach (var root in CandidateRoots())
        {
            var probe = Path.Combine(root, "src", "RufusMapEditor.AiBackend", "Properties", "launchSettings.json");
            if (File.Exists(probe))
            {
                path = Path.GetFullPath(probe);
                return true;
            }

            if (root.Contains("RufusMapEditor.AiBackend", StringComparison.OrdinalIgnoreCase))
            {
                var alt = Path.Combine(root, "Properties", "launchSettings.json");
                if (File.Exists(alt))
                {
                    path = Path.GetFullPath(alt);
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryReadApplicationUrl(string launchSettingsPath, out string applicationUrl)
    {
        applicationUrl = "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles))
                return false;

            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.TryGetProperty("applicationUrl", out var urlProp)
                    && urlProp.ValueKind == JsonValueKind.String)
                {
                    var value = urlProp.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        applicationUrl = value!;
                        return true;
                    }
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? dir = null;
            try { dir = new DirectoryInfo(Path.GetFullPath(start)); }
            catch { continue; }

            for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
            {
                if (seen.Add(dir.FullName))
                    yield return dir.FullName;
            }
        }
    }
}
