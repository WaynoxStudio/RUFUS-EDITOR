namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A — HTTP transport abstraction (Editor → RUFUS backend only).
/// Implementations must never target api.openai.com or carry OpenAI API keys.
/// RUFUS access tokens (AI.6C) may be sent as Authorization: Bearer.
/// </summary>
public interface IAiBackendTransport
{
    Task<AiBackendTransportResult> PostJsonAsync(
        Uri endpoint,
        string jsonBody,
        TimeSpan timeout,
        AiBackendRequestAuth auth,
        CancellationToken cancellationToken);
}

public sealed class AiBackendTransportResult
{
    public bool Ok { get; init; }
    public int? StatusCode { get; init; }
    public string? Body { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static AiBackendTransportResult Success(int statusCode, string body) => new()
    {
        Ok = statusCode is >= 200 and < 300,
        StatusCode = statusCode,
        Body = body,
        ErrorCode = statusCode switch
        {
            >= 200 and < 300 => null,
            401 => AiServiceCallResult.CodeUnauthorized,
            _ => AiServiceCallResult.CodeHttpError
        },
        ErrorMessage = statusCode switch
        {
            >= 200 and < 300 => null,
            401 => AiBackendGenerationService.UnauthorizedUserMessage,
            _ => $"HTTP {statusCode}"
        }
    };

    public static AiBackendTransportResult Fail(string code, string message) => new()
    {
        Ok = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}
