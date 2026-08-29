using System.Collections.Concurrent;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A — safe activity log for AI generation. Never records Authorization, API keys, or tokens.
/// </summary>
public static class AiGenerationActivityLog
{
    private static readonly ConcurrentQueue<string> Lines = new();
    private const int MaxLines = 100;

    public static bool Enabled { get; set; } = true;

    public static IReadOnlyList<string> Snapshot() => Lines.ToArray();

    public static void Clear()
    {
        while (Lines.TryDequeue(out _)) { }
    }

    public static void Info(string message) => Append("IA → " + Sanitize(message));

    public static void Backend(string message) => Append("Backend → " + Sanitize(message));

    public static void Response(string message) => Append("Respuesta → " + Sanitize(message));

    public static void Validation(AiCreativeAction action, bool ok, string? detail)
    {
        var label = AiCreativeRequestPreview.FormatAction(action);
        Append(ok
            ? $"Validación AI.3 → OK ({label})"
            : $"Validación AI.3 → ERROR ({label}): {Sanitize(detail ?? "")}");
    }

    public static void Error(AiCreativeAction action, string detail) =>
        Append($"Error → {AiCreativeRequestPreview.FormatAction(action)}: {Sanitize(detail)}");

    private static void Append(string line)
    {
        if (!Enabled) return;
        Lines.Enqueue(line);
        while (Lines.Count > MaxLines && Lines.TryDequeue(out _)) { }
    }

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (LooksLikeSecret(text))
            return "[omitido: posible secreto]";
        return text;
    }

    private static bool LooksLikeSecret(string text) =>
        text.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
        || text.Contains("RUFUS_AI_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("api_key", StringComparison.OrdinalIgnoreCase)
        || text.Contains("apiKey", StringComparison.OrdinalIgnoreCase)
        || text.Contains("sk-", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Bearer ", StringComparison.OrdinalIgnoreCase);
}
