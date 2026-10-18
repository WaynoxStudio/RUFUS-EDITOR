using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RufusMapEditor.AiBackend;

namespace RufusMapEditor.AiBackend.OpenAi;

/// <summary>
/// AI.4B — calls OpenAI Responses API (POST /v1/responses) with Structured Outputs.
/// Lives only in the backend project — never in the WPF editor.
/// </summary>
public sealed class OpenAiResponsesClient : IOpenAiResponsesClient
{
    public const string ResponsesEndpoint = "https://api.openai.com/v1/responses";

    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;

    public OpenAiResponsesClient(HttpClient http, OpenAiOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<OpenAiResponsesCallResult> CreateStructuredAsync(
        string model,
        string inputText,
        string schemaName,
        JsonElement schema,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
            return OpenAiResponsesCallResult.Fail(
                AiBackendErrorCodes.AiNotConfigured,
                "OPENAI_API_KEY no configurada.");

        if (timeout <= TimeSpan.Zero)
            timeout = _options.Timeout;

        try
        {
            var payload = new JsonObject
            {
                ["model"] = model,
                ["input"] = inputText,
                ["text"] = new JsonObject
                {
                    ["format"] = new JsonObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = schemaName,
                        ["strict"] = true,
                        ["schema"] = JsonNode.Parse(schema.GetRawText())
                    }
                }
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized
                || response.StatusCode == HttpStatusCode.Forbidden)
            {
                LogHttpFailure("OPENAI_AUTH_ERROR", (int)response.StatusCode, body);
                return OpenAiResponsesCallResult.Fail(
                    AiBackendErrorCodes.OpenAiAuthError,
                    "OpenAI rechazó la autenticación.");
            }

            if ((int)response.StatusCode == 429)
            {
                LogHttpFailure("OPENAI_RATE_LIMIT", 429, body);
                return OpenAiResponsesCallResult.Fail(
                    AiBackendErrorCodes.OpenAiRateLimit,
                    "OpenAI rate limit.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode is >= 400 and < 500
                    ? AiBackendErrorCodes.OpenAiBadRequest
                    : AiBackendErrorCodes.OpenAiError;
                LogHttpFailure(code, (int)response.StatusCode, body);
                return OpenAiResponsesCallResult.Fail(
                    code,
                    $"OpenAI HTTP {(int)response.StatusCode}.");
            }

            return ParseSuccessBody(body, model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AiBackendSafeLog.Error("OpenAI client fase=cancelado");
            return OpenAiResponsesCallResult.Fail(AiBackendErrorCodes.OpenAiTimeout, "Cancelado.");
        }
        catch (OperationCanceledException)
        {
            AiBackendSafeLog.Error("OpenAI client fase=timeout");
            return OpenAiResponsesCallResult.Fail(AiBackendErrorCodes.OpenAiTimeout, "Timeout OpenAI.");
        }
        catch (HttpRequestException ex)
        {
            AiBackendSafeLog.Error(
                $"OpenAI client fase=http_request ex={ex.GetType().Name} status={ex.StatusCode?.ToString() ?? "-"} msg={SafeExMsg(ex)}");
            return OpenAiResponsesCallResult.Fail(
                AiBackendErrorCodes.OpenAiConnectionError,
                "Error de red OpenAI.");
        }
        catch (IOException ex)
        {
            // .NET HttpIOException / HttpProtocolException derive from IOException, not HttpRequestException.
            AiBackendSafeLog.Error(
                $"OpenAI client fase=io ex={ex.GetType().Name} msg={SafeExMsg(ex)}");
            return OpenAiResponsesCallResult.Fail(
                AiBackendErrorCodes.OpenAiConnectionError,
                "Error de conexión OpenAI.");
        }
        catch (ArgumentException ex)
        {
            AiBackendSafeLog.Error(
                $"OpenAI client fase=auth_header ex={ex.GetType().Name} msg={SafeExMsg(ex)}");
            return OpenAiResponsesCallResult.Fail(
                AiBackendErrorCodes.OpenAiAuthError,
                "Configuración OpenAI inválida.");
        }
        catch (JsonException ex)
        {
            AiBackendSafeLog.Error(
                $"OpenAI client fase=schema_json ex={ex.GetType().Name} msg={SafeExMsg(ex)}");
            return OpenAiResponsesCallResult.Fail(
                AiBackendErrorCodes.OpenAiBadRequest,
                "Schema OpenAI inválido.");
        }
        catch (Exception ex)
        {
            AiBackendSafeLog.Error(
                $"OpenAI client fase=unexpected ex={ex.GetType().Name} inner={ex.InnerException?.GetType().Name ?? "-"} msg={SafeExMsg(ex)}");
            return OpenAiResponsesCallResult.Fail(
                AiBackendErrorCodes.OpenAiClientError,
                "Error interno OpenAI client.");
        }
    }

    internal static OpenAiResponsesCallResult ParseSuccessBody(string body, string model)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            int? inTok = null;
            int? outTok = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt32(out var iv))
                    inTok = iv;
                if (usage.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt32(out var ov))
                    outTok = ov;
            }

            if (TryExtractRefusal(root, out var refusal) && !string.IsNullOrWhiteSpace(refusal))
            {
                return OpenAiResponsesCallResult.Fail(
                    AiBackendErrorCodes.InvalidAiResponse,
                    "El modelo rechazó la solicitud.",
                    refusal);
            }

            if (!TryExtractOutputText(root, out var text) || string.IsNullOrWhiteSpace(text))
            {
                AiBackendSafeLog.Error(
                    $"OpenAI client fase=parse_output outChars={body.Length} missing_structured_text");
                return OpenAiResponsesCallResult.Fail(
                    AiBackendErrorCodes.OpenAiResponseInvalid,
                    "Respuesta OpenAI incompleta o sin texto estructurado.");
            }

            using var probe = JsonDocument.Parse(text);
            if (probe.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OpenAiResponsesCallResult.Fail(
                    AiBackendErrorCodes.OpenAiResponseInvalid,
                    "Structured Output inesperado (no es objeto JSON).");
            }

            return OpenAiResponsesCallResult.Ok(text, model, inTok, outTok);
        }
        catch (JsonException ex)
        {
            AiBackendSafeLog.Error(
                $"OpenAI client fase=parse_json ex={ex.GetType().Name} outChars={body.Length}");
            return OpenAiResponsesCallResult.Fail(
                AiBackendErrorCodes.OpenAiResponseInvalid,
                "JSON OpenAI ilegible.");
        }
    }

    private static void LogHttpFailure(string code, int status, string body)
    {
        var openAiCode = TryExtractOpenAiErrorCode(body);
        AiBackendSafeLog.Error(
            $"OpenAI client fase=http_status code={code} http={status} " +
            $"openaiCode={openAiCode ?? "-"} bodyChars={body?.Length ?? 0}");
    }

    private static string? TryExtractOpenAiErrorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.Object
                && err.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                var value = code.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    return null;
                return value.Length > 64 ? value[..64] : value;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string SafeExMsg(Exception ex)
    {
        var msg = AiBackendSafeLog.Sanitize(ex.Message);
        if (msg.Length > 160)
            msg = msg[..160];
        return msg;
    }

    private static bool TryExtractOutputText(JsonElement root, out string? text)
    {
        text = null;
        if (root.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
        {
            text = ot.GetString();
            return !string.IsNullOrWhiteSpace(text);
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in output.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type)
                && type.GetString() is "message"
                && item.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in content.EnumerateArray())
                {
                    var partType = part.TryGetProperty("type", out var pt) ? pt.GetString() : null;
                    if (partType is "output_text" or "text"
                        && part.TryGetProperty("text", out var t)
                        && t.ValueKind == JsonValueKind.String)
                    {
                        text = t.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryExtractRefusal(JsonElement root, out string? refusal)
    {
        refusal = null;
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var t) && t.GetString() == "refusal"
                    && part.TryGetProperty("refusal", out var r)
                    && r.ValueKind == JsonValueKind.String)
                {
                    refusal = r.GetString();
                    return true;
                }
            }
        }

        return false;
    }
}
