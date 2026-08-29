using System.Collections.Concurrent;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.3 — debug log for validation OK/ERROR. Never stores API keys or Authorization headers.
/// </summary>
public static class AiResponseDebugLog
{
    private static readonly ConcurrentQueue<AiResponseDebugEntry> Entries = new();
    private const int MaxEntries = 50;

    public static bool Enabled { get; set; } = true;

    public static IReadOnlyList<AiResponseDebugEntry> Snapshot() => Entries.ToArray();

    public static void Clear()
    {
        while (Entries.TryDequeue(out _)) { }
    }

    public static void Log(AiCreativeAction action, bool ok, string? detail, string? rawJson)
    {
        if (!Enabled) return;

        // Never keep secrets even if somehow present in future payloads.
        var safeJson = Sanitize(rawJson);
        Entries.Enqueue(new AiResponseDebugEntry(
            DateTimeOffset.UtcNow,
            action,
            ok,
            detail,
            safeJson));

        while (Entries.Count > MaxEntries && Entries.TryDequeue(out _)) { }
    }

    private static string? Sanitize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        if (json.Contains("api", StringComparison.OrdinalIgnoreCase)
            && (json.Contains("key", StringComparison.OrdinalIgnoreCase)
                || json.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
                || json.Contains("sk-", StringComparison.OrdinalIgnoreCase)))
            return "[omitido: posible secreto]";
        return json;
    }
}

public sealed record AiResponseDebugEntry(
    DateTimeOffset AtUtc,
    AiCreativeAction Action,
    bool Ok,
    string? Detail,
    string? RawJson);
