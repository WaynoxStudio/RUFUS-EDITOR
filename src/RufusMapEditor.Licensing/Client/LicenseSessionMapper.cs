using RufusMapEditor.Licensing.Contracts;

namespace RufusMapEditor.Licensing.Client;

public static class LicenseSessionMapper
{
    public static LicenseSessionLocalState FromSuccess(SessionSuccessResponse session, string deviceId) =>
        new()
        {
            SessionToken = session.SessionToken,
            LeaseExpiresAt = session.ExpiresAt,
            LicenseExpiresAt = session.LicenseExpiresAt,
            PermissionEditor = session.Permissions.Editor,
            PermissionAi = session.Permissions.Ai,
            DeviceId = deviceId,
            AiDailyLimit = session.AiDailyLimit,
            AiMonthlyLimit = session.AiMonthlyLimit,
            AiUsageToday = session.AiUsageToday,
            AiUsageMonth = session.AiUsageMonth,
        };
}
