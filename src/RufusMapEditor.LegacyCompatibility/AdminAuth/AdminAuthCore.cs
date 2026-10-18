namespace RufusMapEditor.LegacyCompatibility.AdminAuth;

/// <summary>Admin API routes — private; never public Editor dist.</summary>
public static class AdminApiRoutes
{
    public const string Prefix = "/v1/admin";

    /// <summary>Issue a short-lived Admin AI session token (not the Admin API secret).</summary>
    public const string CreateAiSession = "/v1/admin/ai-session";
}

/// <summary>
/// Admin auth: strong secret only on backend (env RUFUS_ADMIN_API_SECRET).
/// Admin tool stores its own protected credential locally — never in Editor dist.
/// </summary>
public static class AdminAuthOptions
{
    public const string EnvironmentVariable = "RUFUS_ADMIN_API_SECRET";
}

public interface IAdminCredentialVerifier
{
    bool IsConfigured { get; }
    bool Verify(string? presentedSecret);
}

public sealed class EnvironmentAdminCredentialVerifier : IAdminCredentialVerifier
{
    private readonly string _expected;

    public EnvironmentAdminCredentialVerifier(string? expectedFromEnv = null)
    {
        _expected = (expectedFromEnv
                     ?? Environment.GetEnvironmentVariable(AdminAuthOptions.EnvironmentVariable)
                     ?? "").Trim();
    }

    public bool IsConfigured => _expected.Length >= 16;

    public bool Verify(string? presentedSecret)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(presentedSecret))
            return false;
        var a = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_expected));
        var b = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(presentedSecret.Trim()));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}
