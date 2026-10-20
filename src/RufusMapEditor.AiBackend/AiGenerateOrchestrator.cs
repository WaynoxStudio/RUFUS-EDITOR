using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RufusMapEditor.AiBackend.OpenAi;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.AiBackend;

/// <summary>
/// Orchestrates Editor request → OpenAI Structured Output → validation.
/// </summary>
public sealed class AiGenerateOrchestrator
{
    private readonly OpenAiOptions _options;
    private readonly IOpenAiResponsesClient _openAi;

    public AiGenerateOrchestrator(OpenAiOptions options, IOpenAiResponsesClient openAi)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
    }

    public Task<AiBackendHttpResponse> GenerateAsync(
        AiBackendGenerateRequest? request,
        CancellationToken cancellationToken) =>
        GenerateAsync(request, adminAuth: false, cancellationToken);

    public async Task<AiBackendHttpResponse> GenerateAsync(
        AiBackendGenerateRequest? request,
        bool adminAuth,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (request is null)
                return Fail(null, AiBackendErrorCodes.InvalidRequest, "Request vacío.");

            if (request.Version != AiBackendGenerateRequest.CurrentVersion)
                return Fail(request.Action, AiBackendErrorCodes.InvalidRequest, "version no soportada.");

            if (string.IsNullOrWhiteSpace(request.Action)
                || !AiBackendWireActions.TryParse(request.Action, out var action))
                return Fail(request.Action, AiBackendErrorCodes.InvalidAction, "action no permitida.");

            if (request.CreativeRequest is null || request.Prompt is null)
                return Fail(request.Action, AiBackendErrorCodes.InvalidRequest, "creativeRequest/prompt requeridos.");

            if (string.IsNullOrWhiteSpace(request.Prompt.Master)
                && string.IsNullOrWhiteSpace(request.Prompt.Context)
                && string.IsNullOrWhiteSpace(request.Prompt.Task))
                return Fail(request.Action, AiBackendErrorCodes.InvalidRequest, "prompt vacío.");

            if (!_options.IsConfigured)
            {
                AiBackendSafeLog.Info($"acción={request.Action} modelo={_options.Model} resultado=AI_NOT_CONFIGURED");
                return Fail(request.Action, AiBackendErrorCodes.AiNotConfigured,
                    "OPENAI_API_KEY no configurada en el backend.");
            }

            var wire = AiBackendWireActions.ToWire(action);

            if (adminAuth)
                AiBackendSafeLog.Info($"AI ADMIN → {wire}");

            var (schemaName, schemaObj) = AiOpenAiStrictSchema.ForAction(action);
            using var schemaDoc = JsonDocument.Parse(schemaObj.ToJsonString());
            var input = BuildModelInput(request.Prompt);
            var openAiTimeout = _options.ResolveTimeout(wire);
            var inputBytes = Encoding.UTF8.GetByteCount(input);

            AiBackendSafeLog.Info(
                $"acción={wire} modelo={_options.Model} inputBytes={inputBytes} " +
                $"openAiTimeoutSec={openAiTimeout.TotalSeconds:0} → OpenAI Responses");

            var openAiSw = Stopwatch.StartNew();
            var openAi = await _openAi.CreateStructuredAsync(
                    _options.Model,
                    input,
                    schemaName,
                    schemaDoc.RootElement.Clone(),
                    openAiTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            openAiSw.Stop();

            AiBackendSafeLog.Info(
                $"acción={wire} fase=openaiMs={openAiSw.ElapsedMilliseconds} " +
                $"ok={openAi.Success} code={openAi.ErrorCode ?? "-"}" +
                FormatTokens(openAi.InputTokens, openAi.OutputTokens));

            if (!openAi.Success)
            {
                var code = openAi.ErrorCode ?? AiBackendErrorCodes.OpenAiError;
                AiBackendSafeLog.Error(
                    $"acción={wire} modelo={_options.Model} duraciónMs={sw.ElapsedMilliseconds} " +
                    $"openaiMs={openAiSw.ElapsedMilliseconds} resultado={code}");
                return Fail(wire, code, openAi.ErrorMessage ?? "Error OpenAI.");
            }

            var validateSw = Stopwatch.StartNew();
            var validated = AiResponseValidator.ParseAndValidate(action, openAi.OutputJson!);
            validateSw.Stop();
            if (!validated.IsValid)
            {
                AiBackendSafeLog.Error(
                    $"acción={wire} modelo={_options.Model} duraciónMs={sw.ElapsedMilliseconds} " +
                    $"validateMs={validateSw.ElapsedMilliseconds} outChars={openAi.OutputJson?.Length ?? 0} " +
                    "resultado=INVALID_AI_RESPONSE");
                return Fail(wire, AiBackendErrorCodes.InvalidAiResponse, "Respuesta IA no válida tras validación backend.");
            }

            var resultElement = ToResultElement(validated);
            var tokenInfo = FormatTokens(openAi.InputTokens, openAi.OutputTokens);
            AiBackendSafeLog.Info(
                $"acción={wire} modelo={_options.Model} duraciónMs={sw.ElapsedMilliseconds} " +
                $"openaiMs={openAiSw.ElapsedMilliseconds} validateMs={validateSw.ElapsedMilliseconds} " +
                $"outChars={openAi.OutputJson?.Length ?? 0} resultado=OK{tokenInfo}");

            return new AiBackendHttpResponse
            {
                Success = true,
                Action = wire,
                Result = resultElement
            };
        }
        catch (OperationCanceledException)
        {
            AiBackendSafeLog.Error($"duraciónMs={sw.ElapsedMilliseconds} resultado=OPENAI_TIMEOUT");
            return Fail(request?.Action, AiBackendErrorCodes.OpenAiTimeout, "Timeout o cancelación.");
        }
        catch (Exception ex)
        {
            AiBackendSafeLog.Error($"INTERNAL_ERROR duraciónMs={sw.ElapsedMilliseconds} msg={ex.GetType().Name}");
            return Fail(request?.Action, AiBackendErrorCodes.InternalError, "Error interno del backend.");
        }
    }

    private static string BuildModelInput(AiBackendPromptDto prompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MASTER ===");
        sb.AppendLine(prompt.Master?.Trim() ?? "");
        sb.AppendLine();
        sb.AppendLine("=== CONTEXT ===");
        sb.AppendLine(prompt.Context?.Trim() ?? "");
        sb.AppendLine();
        sb.AppendLine("=== TASK ===");
        sb.AppendLine(prompt.Task?.Trim() ?? "");
        sb.AppendLine();
        sb.AppendLine("Responde únicamente con el JSON estructurado requerido por el schema.");
        return sb.ToString();
    }

    private static JsonElement ToResultElement(AiGenerationResult validated)
    {
        object payload = validated.Action switch
        {
            AiCreativeAction.GenerarNombre => validated.Names!,
            AiCreativeAction.GenerarDialogo => validated.Dialogue!,
            AiCreativeAction.GenerarConversacion => validated.Conversation!,
            _ => throw new InvalidOperationException("Acción inesperada.")
        };
        var json = AiResponseSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string FormatTokens(int? input, int? output)
    {
        if (input is null && output is null) return "";
        return $" tokensIn={input?.ToString() ?? "?"} tokensOut={output?.ToString() ?? "?"}";
    }

    private static AiBackendHttpResponse Fail(string? action, string code, string message) => new()
    {
        Success = false,
        Action = string.IsNullOrWhiteSpace(action) ? null : action,
        Error = new AiBackendErrorBody { Code = code, Message = message }
    };
}

/// <summary>Wire response matching AI.4A Editor contract.</summary>
public sealed class AiBackendHttpResponse
{
    public bool Success { get; set; }
    public string? Action { get; set; }
    public JsonElement? Result { get; set; }
    public AiBackendErrorBody? Error { get; set; }
}

public sealed class AiBackendErrorBody
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
