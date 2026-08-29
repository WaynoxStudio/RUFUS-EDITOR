using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RufusMapEditor.AiBackend.OpenAi;
using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Services;

namespace RufusMapEditor.AiBackend;

/// <summary>
/// AI.4B / LIC.6 — orchestrates Editor request → quota → OpenAI Structured Output → validation.
/// </summary>
public sealed class AiGenerateOrchestrator
{
    private readonly OpenAiOptions _options;
    private readonly IOpenAiResponsesClient _openAi;
    private readonly AiQuotaService _quota;

    public AiGenerateOrchestrator(OpenAiOptions options, IOpenAiResponsesClient openAi, AiQuotaService quota)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _quota = quota ?? throw new ArgumentNullException(nameof(quota));
    }

    public Task<AiBackendHttpResponse> GenerateAsync(
        AiBackendGenerateRequest? request,
        LicenseAiAuthContext? licenseAi,
        bool legacyAuth,
        CancellationToken cancellationToken) =>
        GenerateAsync(request, licenseAi, legacyAuth, adminAuth: false, cancellationToken);

    public async Task<AiBackendHttpResponse> GenerateAsync(
        AiBackendGenerateRequest? request,
        LicenseAiAuthContext? licenseAi,
        bool legacyAuth,
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

            // Quota only for license session auth (legacy / admin = no USER license quota).
            if (licenseAi is not null && !legacyAuth && !adminAuth)
            {
                var (allowed, denyCode) = await _quota.TryConsumeForGenerationAsync(licenseAi, cancellationToken)
                    .ConfigureAwait(false);
                if (!allowed)
                {
                    AiBackendSafeLog.Info("IA QUOTA → denegado");
                    var (code, msg) = MapQuota(denyCode);
                    return Fail(wire, code, msg);
                }

                AiBackendSafeLog.Info("IA QUOTA → permitido");
            }

            var (schemaName, schemaObj) = AiOpenAiStrictSchema.ForAction(action);
            using var schemaDoc = JsonDocument.Parse(schemaObj.ToJsonString());
            var input = BuildModelInput(request.Prompt);

            AiBackendSafeLog.Info($"acción={wire} modelo={_options.Model} → OpenAI Responses");

            var openAi = await _openAi.CreateStructuredAsync(
                    _options.Model,
                    input,
                    schemaName,
                    schemaDoc.RootElement.Clone(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (licenseAi is not null && !legacyAuth && !adminAuth)
            {
                try
                {
                    await _quota.RecordUsageEventAsync(
                            licenseAi,
                            wire,
                            _options.Model,
                            openAi.InputTokens,
                            openAi.OutputTokens,
                            openAi.Success,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Telemetry must not fail the user response after OpenAI already ran.
                }
            }

            if (!openAi.Success)
            {
                var code = openAi.ErrorCode ?? AiBackendErrorCodes.OpenAiError;
                AiBackendSafeLog.Error(
                    $"acción={wire} modelo={_options.Model} duraciónMs={sw.ElapsedMilliseconds} resultado={code}");
                return Fail(wire, code, openAi.ErrorMessage ?? "Error OpenAI.");
            }

            var validated = AiResponseValidator.ParseAndValidate(action, openAi.OutputJson!);
            if (!validated.IsValid)
            {
                AiBackendSafeLog.Error(
                    $"acción={wire} modelo={_options.Model} duraciónMs={sw.ElapsedMilliseconds} resultado=INVALID_AI_RESPONSE");
                return Fail(wire, AiBackendErrorCodes.InvalidAiResponse, "Respuesta IA no válida tras validación backend.");
            }

            var resultElement = ToResultElement(validated);
            var tokenInfo = FormatTokens(openAi.InputTokens, openAi.OutputTokens);
            AiBackendSafeLog.Info(
                $"acción={wire} modelo={_options.Model} duraciónMs={sw.ElapsedMilliseconds} resultado=OK{tokenInfo}");

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

    private static (string Code, string Message) MapQuota(string? denyCode) => denyCode switch
    {
        LicenseErrorCodes.AiQuotaDailyExceeded => (
            AiBackendErrorCodes.AiQuotaDailyExceeded,
            "Has alcanzado el límite diario de generaciones IA de tu licencia."),
        LicenseErrorCodes.AiQuotaMonthlyExceeded => (
            AiBackendErrorCodes.AiQuotaMonthlyExceeded,
            "Has alcanzado el límite mensual de generaciones IA de tu licencia."),
        _ => (
            AiBackendErrorCodes.AiQuotaExceeded,
            "Has alcanzado el límite de generaciones IA de tu licencia."),
    };

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
