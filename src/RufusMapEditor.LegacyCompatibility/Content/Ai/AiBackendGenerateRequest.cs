using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.4A — versioned Editor → RUFUS backend request. Creative fields only.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiBackendGenerateRequest
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    /// <summary>One of: generate_name | generate_dialogue | generate_conversation.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("creativeRequest")]
    public AiBackendCreativeRequestDto CreativeRequest { get; set; } = new();

    [JsonPropertyName("prompt")]
    public AiBackendPromptDto Prompt { get; set; } = new();
}

/// <summary>Serializable creative payload (no technical IDs).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiBackendCreativeRequestDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("attitude")]
    public string Attitude { get; set; } = "";

    [JsonPropertyName("narrativeContext")]
    public string NarrativeContext { get; set; } = "";

    [JsonPropertyName("additionalInstruction")]
    public string AdditionalInstruction { get; set; } = "";

    [JsonPropertyName("length")]
    public string Length { get; set; } = "corta";

    [JsonPropertyName("style")]
    public string Style { get; set; } = "";

    [JsonPropertyName("currentNpcName")]
    public string CurrentNpcName { get; set; } = "";
}

/// <summary>Composed prompt blocks from AI.2.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AiBackendPromptDto
{
    [JsonPropertyName("master")]
    public string Master { get; set; } = "";

    [JsonPropertyName("context")]
    public string Context { get; set; } = "";

    [JsonPropertyName("task")]
    public string Task { get; set; } = "";
}
