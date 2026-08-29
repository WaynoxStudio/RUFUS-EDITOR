using System.Text.Json;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A — parses backend envelope and re-validates payload with AiResponseValidator (AI.3).
/// </summary>
public static class AiBackendResponseParser
{
    public const string InvalidUserMessage = "Respuesta IA no válida.";

    public static AiServiceCallResult ParseAndValidate(
        AiCreativeAction expectedAction,
        string? responseBody,
        AiBackendGenerateRequest outbound)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            AiGenerationActivityLog.Error(expectedAction, "Respuesta HTTP vacía");
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeInvalidHttp,
                InvalidUserMessage + " (respuesta vacía)",
                outbound);
        }

        if (!AiResponseSerializer.TryDeserialize<AiBackendGenerateResponse>(responseBody, out var envelope, out var serError)
            || envelope is null)
        {
            // Envelope may use flexible result — try JsonDocument for success/error without strict unmapped.
            if (!TryParseEnvelopeLoose(responseBody, out envelope, out serError) || envelope is null)
            {
                AiGenerationActivityLog.Error(expectedAction, "JSON corrupto: " + serError);
                return AiServiceCallResult.Fail(
                    expectedAction,
                    AiServiceCallResult.CodeCorruptJson,
                    InvalidUserMessage + " (JSON corrupto)",
                    outbound);
            }
        }

        if (!envelope.Success)
        {
            var code = string.IsNullOrWhiteSpace(envelope.Error?.Code)
                ? AiServiceCallResult.CodeUnavailable
                : envelope.Error!.Code;
            var msg = string.IsNullOrWhiteSpace(envelope.Error?.Message)
                ? InvalidUserMessage
                : envelope.Error!.Message;
            AiGenerationActivityLog.Error(expectedAction, $"Backend error {code}: {msg}");
            return AiServiceCallResult.Fail(expectedAction, code, msg, outbound);
        }

        if (!AiBackendWireActions.TryParse(envelope.Action, out var wireAction)
            || wireAction != expectedAction)
        {
            AiGenerationActivityLog.Error(expectedAction, "Action incorrecta: " + (envelope.Action ?? "(null)"));
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeWrongAction,
                InvalidUserMessage + " (action incorrecta)",
                outbound);
        }

        if (envelope.Result is null || envelope.Result.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            AiGenerationActivityLog.Error(expectedAction, "Falta result");
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeInvalidAi3,
                InvalidUserMessage,
                outbound);
        }

        string resultJson;
        try
        {
            resultJson = envelope.Result.Value.GetRawText();
        }
        catch (Exception ex)
        {
            AiGenerationActivityLog.Error(expectedAction, "result ilegible: " + ex.Message);
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeCorruptJson,
                InvalidUserMessage,
                outbound);
        }

        var validated = AiResponseValidator.ParseAndValidate(expectedAction, resultJson);
        if (!validated.IsValid)
        {
            AiGenerationActivityLog.Validation(expectedAction, ok: false, validated.ErrorDetail);
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeInvalidAi3,
                InvalidUserMessage,
                outbound);
        }

        AiGenerationActivityLog.Validation(expectedAction, ok: true, null);
        return AiServiceCallResult.Ok(expectedAction, validated, outbound);
    }

    private static bool TryParseEnvelopeLoose(
        string json,
        out AiBackendGenerateResponse? envelope,
        out string? error)
    {
        envelope = null;
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Raíz no es objeto.";
                return false;
            }

            var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            string? action = null;
            if (root.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String)
                action = a.GetString();

            JsonElement? result = null;
            if (root.TryGetProperty("result", out var r))
                result = r.Clone();

            AiBackendErrorDto? err = null;
            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                err = new AiBackendErrorDto
                {
                    Code = e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString() ?? ""
                        : "",
                    Message = e.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString() ?? ""
                        : ""
                };
            }

            envelope = new AiBackendGenerateResponse
            {
                Success = success,
                Action = action,
                Result = result,
                Error = err
            };
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
