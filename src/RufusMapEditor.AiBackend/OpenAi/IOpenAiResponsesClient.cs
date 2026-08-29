using System.Text.Json;

namespace RufusMapEditor.AiBackend.OpenAi;

public sealed class OpenAiResponsesCallResult
{
    public bool Success { get; init; }
    public string? OutputJson { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Model { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public string? Refusal { get; init; }

    public static OpenAiResponsesCallResult Ok(
        string outputJson,
        string model,
        int? inputTokens,
        int? outputTokens) => new()
    {
        Success = true,
        OutputJson = outputJson,
        Model = model,
        InputTokens = inputTokens,
        OutputTokens = outputTokens
    };

    public static OpenAiResponsesCallResult Fail(string code, string message, string? refusal = null) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
        Refusal = refusal
    };
}

public interface IOpenAiResponsesClient
{
    Task<OpenAiResponsesCallResult> CreateStructuredAsync(
        string model,
        string inputText,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken);
}
