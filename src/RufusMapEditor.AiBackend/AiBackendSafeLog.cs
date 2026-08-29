using System.Collections.Concurrent;

namespace RufusMapEditor.AiBackend;

/// <summary>AI.4B — backend activity log. Never records API keys or Authorization headers.</summary>
public static class AiBackendSafeLog
{
    private static readonly ConcurrentQueue<string> Lines = new();
    private const int Max = 200;

    public static bool Enabled { get; set; } = true;

    public static IReadOnlyList<string> Snapshot() => Lines.ToArray();

    public static void Clear()
    {
        while (Lines.TryDequeue(out _)) { }
    }

    public static void Info(string message) => Append("INFO", message);

    public static void Error(string message) => Append("ERROR", message);

    private static void Append(string level, string message)
    {
        if (!Enabled) return;
        Lines.Enqueue($"{DateTimeOffset.UtcNow:O} [{level}] {Sanitize(message)}");
        while (Lines.Count > Max && Lines.TryDequeue(out _)) { }
    }

    internal static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (LooksSecret(text))
            return "[omitido: posible secreto]";
        return text;
    }

    private static bool LooksSecret(string text) =>
        text.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
        || text.Contains("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase)
        || text.Contains("RUFUS_AI_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("api_key", StringComparison.OrdinalIgnoreCase)
        || text.Contains("apiKey", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
        || text.Contains("sk-", StringComparison.OrdinalIgnoreCase);
}
