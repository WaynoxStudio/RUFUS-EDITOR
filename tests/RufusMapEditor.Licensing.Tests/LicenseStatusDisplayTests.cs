using RufusMapEditor.Licensing.Client;

namespace RufusMapEditor.Licensing.Tests;

public sealed class LicenseStatusDisplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FormatQuota_null_limit_is_sin_limite()
    {
        Assert.Equal("3 / Sin límite", LicenseStatusDisplay.FormatQuota(3, null));
        Assert.Equal("0 / Sin límite", LicenseStatusDisplay.FormatQuota(null, null));
        Assert.Equal("3 / 10", LicenseStatusDisplay.FormatQuota(3, 10));
    }

    [Fact]
    public void FormatRemainingDetail_days_and_hours()
    {
        Assert.Equal("Caduca en 6 días",
            LicenseStatusDisplay.FormatRemainingDetail(Now.AddDays(5.2), Now));
        Assert.Equal("Caduca en 14 horas",
            LicenseStatusDisplay.FormatRemainingDetail(Now.AddHours(13.1), Now));
        Assert.Equal("Caduca hoy",
            LicenseStatusDisplay.FormatRemainingDetail(Now.AddMinutes(30), Now));
        Assert.Equal("Caducada",
            LicenseStatusDisplay.FormatRemainingDetail(Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void FormatHubLabel_includes_remaining_and_stale()
    {
        var session = new LicenseSessionLocalState
        {
            LicenseExpiresAt = Now.AddDays(6),
            PermissionEditor = true,
        };
        Assert.Equal("Licencia: Activa · 6 días restantes",
            LicenseStatusDisplay.FormatHubLabel(session, statusFresh: true, Now));
        Assert.Equal("Licencia: Activa · Estado no actualizado",
            LicenseStatusDisplay.FormatHubLabel(session, statusFresh: false, Now));
    }

    [Fact]
    public void IsExpiringSoon_within_threshold()
    {
        Assert.True(LicenseStatusDisplay.IsExpiringSoon(Now.AddDays(2), Now));
        Assert.False(LicenseStatusDisplay.IsExpiringSoon(Now.AddDays(10), Now));
        Assert.False(LicenseStatusDisplay.IsExpiringSoon(Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void FormatExpiresLocal_uses_licenseExpiresAt_not_duration()
    {
        var expires = new DateTimeOffset(2026, 9, 25, 15, 30, 0, TimeSpan.Zero);
        var text = LicenseStatusDisplay.FormatExpiresLocal(expires);
        // Local conversion may shift calendar day; must contain year and time digits.
        Assert.Contains("2026", text, StringComparison.Ordinal);
        Assert.Contains(":", text, StringComparison.Ordinal);
        Assert.DoesNotContain("—", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPermission_labels()
    {
        Assert.Equal("Permitido", LicenseStatusDisplay.FormatPermission(true));
        Assert.Equal("No permitido", LicenseStatusDisplay.FormatPermission(false));
    }
}
