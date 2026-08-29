using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.3 — spoken NPC line only.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiDialogueResult
{
    [JsonPropertyName("texto")]
    public string Texto { get; set; } = "";
}

/// <summary>AI.3 — GenerateDialogue structured response.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiDialogueGenerationResponse
{
    [JsonPropertyName("dialogo")]
    public AiDialogueResult? Dialogo { get; set; }
}
