namespace RufusMapEditor.Licensing.Options;

/// <summary>
/// LIC.7 — compile-time edition gate. USER builds always enforce licensing;
/// DEVELOPMENT builds opt-in via <see cref="LicenseTestOptions.EnvironmentVariable"/>.
/// </summary>
public static class LicenseEnforcementOptions
{
    /// <summary>True for distributable USER builds (<c>RufusEdition=User</c>).</summary>
    public static bool IsUserBuild =>
#if RUFUS_USER
        true;
#else
        false;
#endif

    /// <summary>True for local development builds (default <c>dotnet run</c>).</summary>
    public static bool IsDevelopmentBuild =>
#if RUFUS_USER
        false;
#else
        true;
#endif

    /// <summary>
    /// USER: always enforced (env cannot disable).
    /// DEVELOPMENT: only when <see cref="LicenseTestOptions.EnvironmentVariable"/> is truthy.
    /// </summary>
    public static bool IsEnforced =>
#if RUFUS_USER
        true;
#else
        LicenseTestOptions.IsTruthy(Environment.GetEnvironmentVariable(LicenseTestOptions.EnvironmentVariable));
#endif

    /// <summary>USER always uses session token for IA; DEVELOPMENT follows <see cref="IsEnforced"/>.</summary>
    public static bool UsesSessionTokenForAi =>
#if RUFUS_USER
        true;
#else
        IsEnforced;
#endif
}
