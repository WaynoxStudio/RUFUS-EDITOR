namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A — result of a generation service call (after backend + AI.3 validation).
/// Never applies content to an NPC draft.
/// </summary>
public sealed class AiServiceCallResult
{
    private AiServiceCallResult(
        bool success,
        AiCreativeAction action,
        AiGenerationResult? generation,
        string? errorCode,
        string? errorMessage,
        AiBackendGenerateRequest? outboundRequest)
    {
        Success = success;
        Action = action;
        Generation = generation;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        OutboundRequest = outboundRequest;
    }

    public bool Success { get; }
    public AiCreativeAction Action { get; }
    public AiGenerationResult? Generation { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    /// <summary>Request that would be / was sent (useful for tests and debug preview).</summary>
    public AiBackendGenerateRequest? OutboundRequest { get; }

    public static AiServiceCallResult Ok(
        AiCreativeAction action,
        AiGenerationResult generation,
        AiBackendGenerateRequest outbound) =>
        new(true, action, generation, null, null, outbound);

    public static AiServiceCallResult Fail(
        AiCreativeAction action,
        string errorCode,
        string errorMessage,
        AiBackendGenerateRequest? outbound = null) =>
        new(false, action, null, errorCode, errorMessage, outbound);

    public const string CodeNotConfigured = "not_configured";
    public const string CodeUnauthorized = "unauthorized";
    public const string CodeTimeout = "timeout";
    public const string CodeCancelled = "cancelled";
    public const string CodeHttpError = "http_error";
    public const string CodeInvalidHttp = "invalid_http";
    public const string CodeCorruptJson = "corrupt_json";
    public const string CodeWrongAction = "wrong_action";
    public const string CodeInvalidAi3 = "invalid_ai3";
    public const string CodeUnavailable = "backend_unavailable";
}
