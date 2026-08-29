using System.Text.Json;
using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.4A — RUFUS backend → Editor response envelope.</summary>
public sealed class AiBackendGenerateResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>Raw JSON element for action-specific AI.3 payload when Success.</summary>
    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public AiBackendErrorDto? Error { get; set; }
}

public sealed class AiBackendErrorDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
