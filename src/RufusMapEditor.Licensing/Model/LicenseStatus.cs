namespace RufusMapEditor.Licensing.Model;

/// <summary>Persisted / logical license status. CADUCADA is also derived from expires_at vs server clock.</summary>
public enum LicenseStatus
{
    Created = 0,
    Active = 1,
    Suspended = 2,
    Revoked = 3,
}

public enum DeviceBindStatus
{
    Bound = 0,
    Reset = 1,
}

public enum SessionStatus
{
    Active = 0,
    Closed = 1,
    Expired = 2,
    Terminated = 3,
}
