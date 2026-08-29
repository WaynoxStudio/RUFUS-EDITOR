namespace RufusMapEditor.Licensing.Options;

/// <summary>
/// V1 lease/heartbeat — centralized, configurable. Defaults documented in docs/LICENSING.md.
/// Env (backend later): RUFUS_LICENSE_LEASE_SECONDS, RUFUS_LICENSE_HEARTBEAT_SECONDS.
/// </summary>
public sealed class LicenseLeaseOptions
{
    public const string LeaseSecondsEnvironmentVariable = "RUFUS_LICENSE_LEASE_SECONDS";
    public const string HeartbeatSecondsEnvironmentVariable = "RUFUS_LICENSE_HEARTBEAT_SECONDS";

    /// <summary>V1 default: session lease lasts 15 minutes without renew.</summary>
    public int LeaseSeconds { get; set; } = 15 * 60;

    /// <summary>V1 default: Editor should heartbeat every 5 minutes (client hint).</summary>
    public int HeartbeatSeconds { get; set; } = 5 * 60;

    public static LicenseLeaseOptions CreateDefault() => new();

    public static LicenseLeaseOptions FromEnvironment()
    {
        var o = CreateDefault();
        if (int.TryParse(Environment.GetEnvironmentVariable(LeaseSecondsEnvironmentVariable), out var lease) && lease > 30)
            o.LeaseSeconds = lease;
        if (int.TryParse(Environment.GetEnvironmentVariable(HeartbeatSecondsEnvironmentVariable), out var hb) && hb > 10)
            o.HeartbeatSeconds = hb;
        return o;
    }
}
