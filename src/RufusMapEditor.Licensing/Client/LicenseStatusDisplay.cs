namespace RufusMapEditor.Licensing.Client;

/// <summary>
/// Display-only formatting for license UI. Never used for authorization decisions.
/// Caducidad shown = <c>licenseExpiresAt</c> from backend, converted with <see cref="DateTimeOffset.ToLocalTime"/> for presentation.
/// </summary>
public static class LicenseStatusDisplay
{
    /// <summary>Visual "caduca pronto" threshold (display only).</summary>
    public const int ExpiringSoonDays = 3;

    public static string FormatExpiresLocal(DateTimeOffset? licenseExpiresAt)
    {
        if (licenseExpiresAt is null)
            return "—";
        // Backend authority: licenseExpiresAt (UTC). Presentation only → local clock.
        return licenseExpiresAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    /// <summary>Friendly remaining text for detail panel (display only).</summary>
    public static string FormatRemainingDetail(DateTimeOffset? licenseExpiresAt, DateTimeOffset utcNow)
    {
        if (licenseExpiresAt is null)
            return "—";

        var remaining = licenseExpiresAt.Value - utcNow;
        if (remaining <= TimeSpan.Zero)
            return "Caducada";

        if (remaining.TotalHours < 1)
            return "Caduca hoy";

        if (remaining.TotalHours < 24)
        {
            var hours = Math.Max(1, (int)Math.Ceiling(remaining.TotalHours));
            return hours == 1 ? "Caduca en 1 hora" : $"Caduca en {hours} horas";
        }

        var days = Math.Max(1, (int)Math.Ceiling(remaining.TotalDays));
        return days == 1 ? "Caduca en 1 día" : $"Caduca en {days} días";
    }

    /// <summary>Short remaining fragment for Hub indicator.</summary>
    public static string? FormatRemainingHub(DateTimeOffset? licenseExpiresAt, DateTimeOffset utcNow)
    {
        if (licenseExpiresAt is null)
            return null;

        var remaining = licenseExpiresAt.Value - utcNow;
        if (remaining <= TimeSpan.Zero)
            return "caducada";

        if (remaining.TotalHours < 1)
            return "caduca hoy";

        if (remaining.TotalHours < 24)
        {
            var hours = Math.Max(1, (int)Math.Ceiling(remaining.TotalHours));
            return hours == 1 ? "1 hora restante" : $"{hours} horas restantes";
        }

        var days = Math.Max(1, (int)Math.Ceiling(remaining.TotalDays));
        return days == 1 ? "1 día restante" : $"{days} días restantes";
    }

    public static string FormatQuota(int? usage, int? limit)
    {
        var usageText = usage?.ToString() ?? "0";
        var limitText = limit is null || limit <= 0 ? "Sin límite" : limit.Value.ToString();
        return $"{usageText} / {limitText}";
    }

    public static bool IsExpiringSoon(DateTimeOffset? licenseExpiresAt, DateTimeOffset utcNow)
    {
        if (licenseExpiresAt is null)
            return false;
        var remaining = licenseExpiresAt.Value - utcNow;
        return remaining > TimeSpan.Zero && remaining <= TimeSpan.FromDays(ExpiringSoonDays);
    }

    public static string FormatHubLabel(
        LicenseSessionLocalState? session,
        bool statusFresh,
        DateTimeOffset utcNow,
        string? deniedStatusLabel = null)
    {
        if (!string.IsNullOrWhiteSpace(deniedStatusLabel))
            return deniedStatusLabel!;

        if (session is null)
            return "Licencia: —";

        if (!statusFresh)
            return "Licencia: Activa · Estado no actualizado";

        var rem = FormatRemainingHub(session.LicenseExpiresAt, utcNow);
        return rem is null
            ? "Licencia: Activa"
            : $"Licencia: Activa · {rem}";
    }

    public static string FormatPermission(bool allowed) =>
        allowed ? "Permitido" : "No permitido";
}
