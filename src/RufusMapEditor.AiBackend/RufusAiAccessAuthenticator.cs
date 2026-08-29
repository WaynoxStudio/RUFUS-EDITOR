using System.Security.Cryptography;
using System.Text;

namespace RufusMapEditor.AiBackend;

/// <summary>
/// AI.6C — validates Authorization: Bearer &lt;RUFUS_AI_ACCESS_TOKEN&gt;.
/// Never logs tokens or the Authorization header value.
/// </summary>
public static class RufusAiAccessAuthenticator
{
    public const string UnauthorizedUserMessage =
        "No autorizado para utilizar el servicio IA de RUFUS.";

    /// <summary>
    /// Returns true when the request is authorized to call generation.
    /// Logs only "IA AUTH → OK" or "IA AUTH → DENEGADA".
    /// </summary>
    public static bool TryAuthorize(HttpRequest request, RufusAiAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured || string.IsNullOrWhiteSpace(options.AccessToken))
        {
            AiBackendSafeLog.Info("IA AUTH → DENEGADA");
            return false;
        }

        if (!TryExtractBearerToken(request, out var presented) || string.IsNullOrEmpty(presented))
        {
            AiBackendSafeLog.Info("IA AUTH → DENEGADA");
            return false;
        }

        if (!FixedTimeEqualsUtf8(presented, options.AccessToken))
        {
            AiBackendSafeLog.Info("IA AUTH → DENEGADA");
            return false;
        }

        AiBackendSafeLog.Info("IA AUTH → OK");
        return true;
    }

    /// <summary>Extracts Bearer token without logging. Returns false if missing/malformed/empty.</summary>
    public static bool TryExtractBearerToken(HttpRequest request, out string token)
    {
        token = "";
        if (!request.Headers.TryGetValue("Authorization", out var values))
            return false;

        var raw = values.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        const string prefix = "Bearer ";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        token = raw[prefix.Length..].Trim();
        return token.Length > 0;
    }

    public static bool FixedTimeEqualsUtf8(string presented, string expected)
    {
        var a = Encoding.UTF8.GetBytes(presented);
        var b = Encoding.UTF8.GetBytes(expected);
        if (a.Length != b.Length)
        {
            // Still compare against self to reduce trivial timing leaks on length mismatch.
            CryptographicOperations.FixedTimeEquals(a, a);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
