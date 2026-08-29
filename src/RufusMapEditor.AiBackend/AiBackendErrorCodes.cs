namespace RufusMapEditor.AiBackend;

/// <summary>AI.4B / LIC.6 — controlled error codes returned to RUFUS Editor (no stack traces).</summary>
public static class AiBackendErrorCodes
{
    public const string AiNotConfigured = "AI_NOT_CONFIGURED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string AiNotAllowed = "AI_NOT_ALLOWED";
    public const string AiQuotaExceeded = "AI_QUOTA_EXCEEDED";
    public const string AiQuotaDailyExceeded = "AI_QUOTA_DAILY_EXCEEDED";
    public const string AiQuotaMonthlyExceeded = "AI_QUOTA_MONTHLY_EXCEEDED";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InvalidAction = "INVALID_ACTION";
    public const string OpenAiError = "OPENAI_ERROR";
    public const string OpenAiTimeout = "OPENAI_TIMEOUT";
    public const string InvalidAiResponse = "INVALID_AI_RESPONSE";
    public const string InternalError = "INTERNAL_ERROR";
}
