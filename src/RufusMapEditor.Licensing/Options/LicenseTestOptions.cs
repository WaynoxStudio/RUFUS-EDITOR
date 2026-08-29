namespace RufusMapEditor.Licensing.Options;

/// <summary>
/// LIC.5 / LIC.7 — DEVELOPMENT-only opt-in via env for integration testing.
/// USER builds ignore this variable (<see cref="LicenseEnforcementOptions.IsEnforced"/>).
/// </summary>
public sealed class LicenseTestOptions
{
    public const string EnvironmentVariable = "RUFUS_LICENSE_TEST";

    /// <summary>True when env is 1 / true / yes / on (case-insensitive).</summary>
    public bool Enabled { get; init; }

    public static LicenseTestOptions FromEnvironment() =>
        new() { Enabled = IsTruthy(Environment.GetEnvironmentVariable(EnvironmentVariable)) };

    public static bool IsTruthy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        return raw.Trim() switch
        {
            "1" => true,
            _ when raw.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
            _ when raw.Equals("yes", StringComparison.OrdinalIgnoreCase) => true,
            _ when raw.Equals("on", StringComparison.OrdinalIgnoreCase) => true,
            _ => false,
        };
    }
}
