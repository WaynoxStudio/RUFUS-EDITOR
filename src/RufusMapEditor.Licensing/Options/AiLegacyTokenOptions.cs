namespace RufusMapEditor.Licensing.Options;

/// <summary>
/// LIC.6 — controlled legacy shared-token compatibility for AI generate.
/// Default OFF. Must be explicitly enabled for RUFUS_AI_ACCESS_TOKEN to authorize /v1/ai/generate.
/// </summary>
public sealed class AiLegacyTokenOptions
{
    public const string EnvironmentVariable = "RUFUS_AI_LEGACY_TOKEN_ENABLED";

    public bool Enabled { get; init; }

    public static AiLegacyTokenOptions FromEnvironment() =>
        new() { Enabled = LicenseTestOptions.IsTruthy(Environment.GetEnvironmentVariable(EnvironmentVariable)) };
}
