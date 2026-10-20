using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.3 — one player reply suggestion (text + descriptive tone).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiPlayerResponseSuggestion
{
    [JsonPropertyName("texto")]
    public string Texto { get; set; } = "";

    /// <summary>Descriptive only: neutral | amable | humoristico | desafiante.</summary>
    [JsonPropertyName("tono")]
    public string Tono { get; set; } = "";
}

/// <summary>AI.3 — conversation creative payload (full mission dialog roles).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiConversationResult
{
    /// <summary>NPC text for OFRECER (opening / PregDar).</summary>
    [JsonPropertyName("textoNpc")]
    public string TextoNpc { get; set; } = "";

    [JsonPropertyName("respuestasJugador")]
    public List<AiPlayerResponseSuggestion> RespuestasJugador { get; set; } = [];

    /// <summary>NPC text for MISIÓN INCOMPLETA (PregIncompleta).</summary>
    [JsonPropertyName("textoIncompleta")]
    public string TextoIncompleta { get; set; } = "";

    /// <summary>NPC text for MISIÓN COMPLETADA (PregCompletada).</summary>
    [JsonPropertyName("textoCompletada")]
    public string TextoCompletada { get; set; } = "";
}

/// <summary>AI.3 — GenerateConversation structured response.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiConversationGenerationResponse
{
    [JsonPropertyName("conversacion")]
    public AiConversationResult? Conversacion { get; set; }
}
