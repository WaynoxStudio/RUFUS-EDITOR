using System.Reflection;
using System.Text;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>AI.3 — structured response contract, validation, schemas (no API).</summary>
public sealed class AiResponseContractTests
{
    private static readonly string[] ForbiddenModelProps =
    [
        "NpcId", "QuestionId", "ResponseId", "QuestId", "StageId", "ItemId",
        "MapId", "CellId", "GfxId", "Action", "Args", "Condition", "DQ", "DA"
    ];

    [Fact]
    public void Valid_three_names_ok()
    {
        var result = AiResponseValidator.ParseAndValidate(
            AiCreativeAction.GenerarNombre, AiMockResponses.NamesJson);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Names);
        Assert.Equal(3, result.Names!.Nombres.Count);
        Assert.Null(result.Dialogue);
        Assert.Null(result.Conversation);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Names_wrong_count_invalid(int count)
    {
        var json = BuildNamesJson(count, name: "Ok");
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarNombre, json);
        Assert.False(result.IsValid);
        Assert.Contains("Resultado IA inválido", result.ErrorDetail!, StringComparison.Ordinal);
        Assert.Null(result.Names);
    }

    [Fact]
    public void Names_empty_nombre_invalid()
    {
        var json = BuildNamesJson(3, name: "  ");
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarNombre, json);
        Assert.False(result.IsValid);
        Assert.Contains("vacío", result.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Names_too_long_invalid()
    {
        var longName = new string('A', AiResponseLimits.MaxNameLength + 1);
        var json = BuildNamesJson(3, name: longName);
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarNombre, json);
        Assert.False(result.IsValid);
        Assert.Contains("supera", result.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Names_technical_property_invalid()
    {
        var json =
            """
            {
              "nombres": [
                { "nombre": "A", "motivo": "m" },
                { "nombre": "B", "motivo": "m" },
                { "nombre": "C", "motivo": "m", "npcId": 12 }
              ]
            }
            """;
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarNombre, json);
        Assert.False(result.IsValid);
        Assert.Null(result.Names);
    }

    [Fact]
    public void Valid_dialogue_ok()
    {
        var result = AiResponseValidator.ParseAndValidate(
            AiCreativeAction.GenerarDialogo, AiMockResponses.DialogueJson);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Dialogue?.Dialogo?.Texto);
        Assert.Null(result.Names);
    }

    [Fact]
    public void Dialogue_empty_invalid()
    {
        var json = """{ "dialogo": { "texto": "   " } }""";
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarDialogo, json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Dialogue_too_long_invalid()
    {
        var text = new string('x', AiResponseLimits.MaxDialogueLength + 1);
        var json = JsonSerializer.Serialize(new { dialogo = new { texto = text } });
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarDialogo, json);
        Assert.False(result.IsValid);
        Assert.Contains("supera", result.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dialogue_technical_field_invalid()
    {
        var json = """{ "dialogo": { "texto": "Hola", "questionId": 9 } }""";
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarDialogo, json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Valid_conversation_ok()
    {
        var result = AiResponseValidator.ParseAndValidate(
            AiCreativeAction.GenerarConversacion, AiMockResponses.ConversationJson);
        Assert.True(result.IsValid);
        Assert.Equal(3, result.Conversation!.Conversacion!.RespuestasJugador.Count);
        Assert.All(result.Conversation.Conversacion.RespuestasJugador,
            r => Assert.True(AiPlayerTone.IsAllowed(r.Tono)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Conversation_wrong_reply_count_invalid(int count)
    {
        var json = BuildConversationJson(count, opening: "Hola", reply: "Ok", tone: "neutral");
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarConversacion, json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Conversation_empty_reply_invalid()
    {
        var json = BuildConversationJson(3, opening: "Hola", reply: "  ", tone: "neutral");
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarConversacion, json);
        Assert.False(result.IsValid);
        Assert.Contains("vacía", result.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Conversation_empty_opening_invalid()
    {
        var json = BuildConversationJson(3, opening: "", reply: "Hola", tone: "amable");
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarConversacion, json);
        Assert.False(result.IsValid);
        Assert.Contains("Apertura", result.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("neutral")]
    [InlineData("amable")]
    [InlineData("humoristico")]
    [InlineData("desafiante")]
    public void Conversation_allowed_tones_ok(string tone)
    {
        var json = BuildConversationJson(3, opening: "Ey", reply: "Respuesta", tone: tone);
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarConversacion, json);
        Assert.True(result.IsValid, result.ErrorDetail);
    }

    [Fact]
    public void Conversation_unknown_tone_invalid()
    {
        var json = BuildConversationJson(3, opening: "Ey", reply: "Respuesta", tone: "agresivo");
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarConversacion, json);
        Assert.False(result.IsValid);
        Assert.Contains("tono", result.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Corrupt_json_invalid()
    {
        var result = AiResponseValidator.ParseAndValidate(
            AiCreativeAction.GenerarNombre, "{ nombres: [");
        Assert.False(result.IsValid);
        Assert.Contains("Resultado IA inválido", result.ErrorDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_response_type_for_action_invalid()
    {
        var asName = AiResponseValidator.ParseAndValidate(
            AiCreativeAction.GenerarNombre, AiMockResponses.DialogueJson);
        Assert.False(asName.IsValid);
        Assert.Null(asName.Names);

        var asDialogue = AiResponseValidator.ParseAndValidate(
            AiCreativeAction.GenerarDialogo, AiMockResponses.NamesJson);
        Assert.False(asDialogue.IsValid);
        Assert.Null(asDialogue.Dialogue);
    }

    [Fact]
    public void Additional_root_property_invalid()
    {
        var json =
            """
            {
              "dialogo": { "texto": "Hola" },
              "mapId": 100
            }
            """;
        var result = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarDialogo, json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Response_models_have_no_technical_id_properties()
    {
        foreach (var type in new[]
                 {
                     typeof(AiNameGenerationResponse), typeof(AiNameSuggestion),
                     typeof(AiDialogueGenerationResponse), typeof(AiDialogueResult),
                     typeof(AiConversationGenerationResponse), typeof(AiConversationResult),
                     typeof(AiPlayerResponseSuggestion)
                 })
        {
            var names = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var forbidden in ForbiddenModelProps)
                Assert.DoesNotContain(forbidden, names);
        }
    }

    [Fact]
    public void Json_schemas_exist_and_forbid_additional_properties()
    {
        Assert.Contains("NameGenerationSchema", AiJsonSchemas.NameGenerationSchema, StringComparison.Ordinal);
        Assert.Contains("DialogueGenerationSchema", AiJsonSchemas.DialogueGenerationSchema, StringComparison.Ordinal);
        Assert.Contains("ConversationGenerationSchema", AiJsonSchemas.ConversationGenerationSchema, StringComparison.Ordinal);
        Assert.Contains("\"additionalProperties\": false", AiJsonSchemas.NameGenerationSchema, StringComparison.Ordinal);
        Assert.Contains("\"minItems\": 3", AiJsonSchemas.NameGenerationSchema, StringComparison.Ordinal);
        Assert.Contains("\"maxItems\": 3", AiJsonSchemas.NameGenerationSchema, StringComparison.Ordinal);
        Assert.Contains("\"additionalProperties\": false", AiJsonSchemas.DialogueGenerationSchema, StringComparison.Ordinal);
        Assert.Contains("\"additionalProperties\": false", AiJsonSchemas.ConversationGenerationSchema, StringComparison.Ordinal);
        Assert.Contains("humoristico", AiJsonSchemas.ConversationGenerationSchema, StringComparison.Ordinal);
        Assert.Equal(typeof(AiNameGenerationResponse), AiJsonSchemas.ExpectedClrType(AiCreativeAction.GenerarNombre));
        Assert.Equal(typeof(AiDialogueGenerationResponse), AiJsonSchemas.ExpectedClrType(AiCreativeAction.GenerarDialogo));
        Assert.Equal(typeof(AiConversationGenerationResponse), AiJsonSchemas.ExpectedClrType(AiCreativeAction.GenerarConversacion));
    }

    [Fact]
    public void Serialization_roundtrip_utf8_preserves_accents()
    {
        var original = new AiDialogueGenerationResponse
        {
            Dialogo = new AiDialogueResult { Texto = "¡Cuidado con la bahía!" }
        };
        var json = AiResponseSerializer.Serialize(original);
        var bytes = AiResponseSerializer.SerializeUtf8Bytes(original);
        Assert.Equal(json, Encoding.UTF8.GetString(bytes));
        Assert.True(AiResponseSerializer.TryDeserialize<AiDialogueGenerationResponse>(json, out var back, out _));
        Assert.Equal(original.Dialogo.Texto, back!.Dialogo!.Texto);
    }

    [Fact]
    public void Debug_log_records_ok_and_error_without_secrets()
    {
        AiResponseDebugLog.Clear();
        AiResponseDebugLog.Enabled = true;
        _ = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarDialogo, AiMockResponses.DialogueJson);
        _ = AiResponseValidator.ParseAndValidate(AiCreativeAction.GenerarDialogo, "{bad");
        var snap = AiResponseDebugLog.Snapshot();
        Assert.Contains(snap, e => e.Ok && e.Action == AiCreativeAction.GenerarDialogo);
        Assert.Contains(snap, e => !e.Ok);
        Assert.DoesNotContain(snap, e => (e.RawJson ?? "").Contains("sk-", StringComparison.Ordinal));
    }

    [Fact]
    public void Result_is_separated_from_application_no_npc_fields()
    {
        var result = AiMockResponses.LoadValidated(AiCreativeAction.GenerarNombre);
        Assert.True(result.IsValid);
        // Contract: result only carries creative payloads — applying to NPC is a later phase.
        Assert.NotNull(result.Names);
        Assert.Null(result.Dialogue);
        Assert.Null(result.Conversation);
        var props = typeof(AiGenerationResult).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("NpcId", props);
        Assert.DoesNotContain("Applied", props);
    }

    [Fact]
    public void Limits_are_documented_constants()
    {
        Assert.Equal(60, AiResponseLimits.MaxNameLength);
        Assert.Equal(180, AiResponseLimits.MaxMotivoLength);
        Assert.Equal(500, AiResponseLimits.MaxDialogueLength);
        Assert.Equal(300, AiResponseLimits.MaxPlayerReplyLength);
        Assert.Equal(3, AiResponseLimits.ExactNameCount);
        Assert.Equal(3, AiResponseLimits.ExactPlayerReplyCount);
    }

    private static string BuildNamesJson(int count, string name)
    {
        var items = Enumerable.Range(0, count)
            .Select(i => $$"""{ "nombre": "{{name}}{{i}}", "motivo": "m{{i}}" }""");
        // empty name case: all entries use the provided name as-is
        if (string.IsNullOrWhiteSpace(name))
            items = Enumerable.Range(0, count).Select(_ => """{ "nombre": "   ", "motivo": "m" }""");
        else if (name.Length > AiResponseLimits.MaxNameLength)
            items = Enumerable.Range(0, count).Select(_ =>
                $$"""{ "nombre": "{{name}}", "motivo": "m" }""");
        return """{ "nombres": [""" + string.Join(",", items) + "] }";
    }

    private static string BuildConversationJson(int replyCount, string opening, string reply, string tone)
    {
        var replies = Enumerable.Range(0, replyCount)
            .Select(_ => $$"""{ "texto": "{{Escape(reply)}}", "tono": "{{tone}}" }""");
        return $$"""
            {
              "conversacion": {
                "textoNpc": "{{Escape(opening)}}",
                "respuestasJugador": [ {{string.Join(",", replies)}} ]
              }
            }
            """;
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
