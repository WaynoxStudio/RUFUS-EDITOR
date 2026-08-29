using System.Text.Json.Nodes;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.3/AI.4B — JSON Schema documents for Structured Outputs (OpenAI Responses via backend).
/// </summary>
public static class AiJsonSchemas
{
    public static string NameGenerationSchema { get; } = BuildNameSchema();
    public static string DialogueGenerationSchema { get; } = BuildDialogueSchema();
    public static string ConversationGenerationSchema { get; } = BuildConversationSchema();

    public static string ForAction(AiCreativeAction action) => action switch
    {
        AiCreativeAction.GenerarNombre => NameGenerationSchema,
        AiCreativeAction.GenerarDialogo => DialogueGenerationSchema,
        AiCreativeAction.GenerarConversacion => ConversationGenerationSchema,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    public static Type ExpectedClrType(AiCreativeAction action) => action switch
    {
        AiCreativeAction.GenerarNombre => typeof(AiNameGenerationResponse),
        AiCreativeAction.GenerarDialogo => typeof(AiDialogueGenerationResponse),
        AiCreativeAction.GenerarConversacion => typeof(AiConversationGenerationResponse),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    private static string BuildNameSchema()
    {
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "NameGenerationSchema",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("nombres"),
            ["properties"] = new JsonObject
            {
                ["nombres"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = AiResponseLimits.ExactNameCount,
                    ["maxItems"] = AiResponseLimits.ExactNameCount,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("nombre"),
                        ["properties"] = new JsonObject
                        {
                            ["nombre"] = StringSchema(1, AiResponseLimits.MaxNameLength),
                            ["motivo"] = StringSchema(0, AiResponseLimits.MaxMotivoLength)
                        }
                    }
                }
            }
        };
        return root.ToJsonString(AiResponseSerializer.Options);
    }

    private static string BuildDialogueSchema()
    {
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "DialogueGenerationSchema",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("dialogo"),
            ["properties"] = new JsonObject
            {
                ["dialogo"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("texto"),
                    ["properties"] = new JsonObject
                    {
                        ["texto"] = StringSchema(1, AiResponseLimits.MaxDialogueLength)
                    }
                }
            }
        };
        return root.ToJsonString(AiResponseSerializer.Options);
    }

    private static string BuildConversationSchema()
    {
        var tones = new JsonArray();
        foreach (var t in AiPlayerTone.Allowed)
            tones.Add(t);

        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "ConversationGenerationSchema",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("conversacion"),
            ["properties"] = new JsonObject
            {
                ["conversacion"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("textoNpc", "respuestasJugador"),
                    ["properties"] = new JsonObject
                    {
                        ["textoNpc"] = StringSchema(1, AiResponseLimits.MaxDialogueLength),
                        ["respuestasJugador"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["minItems"] = AiResponseLimits.ExactPlayerReplyCount,
                            ["maxItems"] = AiResponseLimits.ExactPlayerReplyCount,
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["additionalProperties"] = false,
                                ["required"] = new JsonArray("texto", "tono"),
                                ["properties"] = new JsonObject
                                {
                                    ["texto"] = StringSchema(1, AiResponseLimits.MaxPlayerReplyLength),
                                    ["tono"] = new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["enum"] = tones
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        return root.ToJsonString(AiResponseSerializer.Options);
    }

    private static JsonObject StringSchema(int minLength, int maxLength) => new()
    {
        ["type"] = "string",
        ["minLength"] = minLength,
        ["maxLength"] = maxLength
    };
}
