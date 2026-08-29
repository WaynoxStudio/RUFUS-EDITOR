namespace RufusMapEditor.App.Licensing;

/// <summary>LIC.7 — global gate when license/session becomes invalid during runtime.</summary>
public static class LicenseRuntimeGate
{
    public static bool IsBlocked { get; private set; }

    public static string BlockMessage { get; private set; } = "";

    public static event Action? Blocked;

    public static void Block(string message)
    {
        if (IsBlocked && string.Equals(BlockMessage, message, StringComparison.Ordinal))
            return;

        IsBlocked = true;
        BlockMessage = string.IsNullOrWhiteSpace(message)
            ? "La sesión de licencia ya no es válida."
            : message;
        Blocked?.Invoke();
    }

    public static void Clear()
    {
        IsBlocked = false;
        BlockMessage = "";
    }

    public static bool CanUseEditor =>
        !IsBlocked && (App.License?.CurrentSession?.PermissionEditor ?? !LicenseEnforcementActive);

    private static bool LicenseEnforcementActive =>
        RufusMapEditor.Licensing.Options.LicenseEnforcementOptions.IsEnforced;
}
