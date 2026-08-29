using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.3 — one name suggestion. Creative only.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiNameSuggestion
{
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = "";

    /// <summary>Brief optional rationale (shown in preview).</summary>
    [JsonPropertyName("motivo")]
    public string? Motivo { get; set; }
}

/// <summary>AI.3 — GenerateName structured response. Expects exactly 3 suggestions.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiNameGenerationResponse
{
    [JsonPropertyName("nombres")]
    public List<AiNameSuggestion> Nombres { get; set; } = [];
}
