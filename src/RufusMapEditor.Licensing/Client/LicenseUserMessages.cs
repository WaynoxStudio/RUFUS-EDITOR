using RufusMapEditor.Licensing.Contracts;

namespace RufusMapEditor.Licensing.Client;

/// <summary>Human-readable messages for Editor UI (no raw JSON / secrets).</summary>
public static class LicenseUserMessages
{
    public const string NetworkLost = "Conexión de licencia temporalmente perdida.";
    public const string ServiceUnavailable = "No se pudo contactar con el servicio de licencias.";
    public const string EditorNotAllowed = "Esta licencia no incluye acceso a RUFUS Editor.";
    public const string DeviceLinkedElsewhere = "Esta licencia ya está vinculada al máximo de dispositivos permitidos.";
    public const string SessionAlreadyActive = "Esta licencia ya tiene el máximo de sesiones activas.";
    public const string Suspended = "Tu licencia está suspendida.";
    public const string Revoked = "Esta licencia ha sido revocada.";
    public const string Expired = "Tu licencia ha caducado.";
    public const string SessionInvalid = "La sesión de licencia ya no es válida. Activa de nuevo.";
    public const string NotFound = "Código de licencia no válido.";
    public const string Generic = "No se pudo activar la licencia.";

    public static string ForErrorCode(string? errorCode) => errorCode switch
    {
        LicenseErrorCodes.DeviceLimitReached => DeviceLinkedElsewhere,
        LicenseErrorCodes.SessionLimitReached => SessionAlreadyActive,
        LicenseErrorCodes.LicenseSuspended => Suspended,
        LicenseErrorCodes.LicenseRevoked => Revoked,
        LicenseErrorCodes.LicenseExpired => Expired,
        LicenseErrorCodes.SessionInvalid => SessionInvalid,
        LicenseErrorCodes.DeviceMismatch => DeviceLinkedElsewhere,
        LicenseErrorCodes.EditorNotAllowed => EditorNotAllowed,
        LicenseErrorCodes.AiNotAllowed => "Tu licencia no incluye acceso al Asistente IA.",
        LicenseErrorCodes.AiQuotaExceeded => "Has alcanzado el límite de generaciones IA de tu licencia.",
        LicenseErrorCodes.AiQuotaDailyExceeded => "Has alcanzado el límite diario de generaciones IA de tu licencia.",
        LicenseErrorCodes.AiQuotaMonthlyExceeded => "Has alcanzado el límite mensual de generaciones IA de tu licencia.",
        LicenseErrorCodes.LicenseNotFound => NotFound,
        LicenseErrorCodes.NetworkUnavailable => NetworkLost,
        _ => Generic,
    };

    public static bool IsExplicitRejection(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            return false;
        return errorCode is
            LicenseErrorCodes.LicenseSuspended or
            LicenseErrorCodes.LicenseRevoked or
            LicenseErrorCodes.LicenseExpired or
            LicenseErrorCodes.LicenseInactive or
            LicenseErrorCodes.SessionInvalid or
            LicenseErrorCodes.DeviceMismatch or
            LicenseErrorCodes.DeviceLimitReached or
            LicenseErrorCodes.SessionLimitReached or
            LicenseErrorCodes.EditorNotAllowed or
            LicenseErrorCodes.LicenseNotFound or
            LicenseErrorCodes.AiNotAllowed or
            LicenseErrorCodes.AiQuotaExceeded or
            LicenseErrorCodes.AiQuotaDailyExceeded or
            LicenseErrorCodes.AiQuotaMonthlyExceeded;
    }
}
