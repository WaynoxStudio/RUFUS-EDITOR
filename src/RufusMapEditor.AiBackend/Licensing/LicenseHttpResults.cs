using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Services;

namespace RufusMapEditor.AiBackend.Licensing;

internal static class LicenseHttpResults
{
    public static IResult FromOperation(LicenseOperationResult result)
    {
        if (result.Success && result.Session is not null)
            return Results.Json(result.Session, statusCode: 200);

        var code = result.ErrorCode ?? LicenseErrorCodes.InvalidRequest;
        return Results.Json(new LicenseErrorResponse
        {
            Success = false,
            ErrorCode = code,
            Message = result.Message ?? UserMessage(code)
        }, statusCode: MapStatus(code));
    }

    public static int MapStatus(string code) => code switch
    {
        LicenseErrorCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        LicenseErrorCodes.LicenseNotFound => StatusCodes.Status404NotFound,
        LicenseErrorCodes.LicenseSuspended => StatusCodes.Status403Forbidden,
        LicenseErrorCodes.LicenseRevoked => StatusCodes.Status403Forbidden,
        LicenseErrorCodes.LicenseExpired => StatusCodes.Status403Forbidden,
        LicenseErrorCodes.LicenseInactive => StatusCodes.Status403Forbidden,
        LicenseErrorCodes.DeviceLimitReached => StatusCodes.Status403Forbidden,
        LicenseErrorCodes.SessionLimitReached => StatusCodes.Status403Forbidden,
        LicenseErrorCodes.SessionInvalid => StatusCodes.Status401Unauthorized,
        LicenseErrorCodes.DeviceMismatch => StatusCodes.Status403Forbidden,
        LicenseErrorCodes.EditorNotAllowed => StatusCodes.Status403Forbidden,
        LicenseErrorCodes.AiNotAllowed => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest
    };

    public static string UserMessage(string code) => code switch
    {
        LicenseErrorCodes.LicenseNotFound => "Licencia no encontrada.",
        LicenseErrorCodes.LicenseSuspended => "Licencia suspendida.",
        LicenseErrorCodes.LicenseRevoked => "Licencia revocada.",
        LicenseErrorCodes.LicenseExpired => "Licencia caducada.",
        LicenseErrorCodes.DeviceLimitReached => "Límite de dispositivos alcanzado.",
        LicenseErrorCodes.SessionLimitReached => "Límite de sesiones simultáneas alcanzado.",
        LicenseErrorCodes.SessionInvalid => "Sesión inválida o expirada.",
        LicenseErrorCodes.DeviceMismatch => "Dispositivo no autorizado para esta sesión.",
        _ => "Solicitud de licencia rechazada."
    };
}
