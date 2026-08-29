using System.Text.Json;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.2 — local catalog cache keyed by lang versions / mobs hash.</summary>
public static class VisualLibraryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "visual-library-cache");

    public sealed class MonsterCachePayload
    {
        public int MonstersLangVersion { get; set; }
        public string MobsFingerprint { get; set; } = "";
        public List<MonsterCacheRow> Entries { get; set; } = new();
    }

    public sealed class MonsterCacheRow
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public int GfxId { get; set; }
        public List<int> Levels { get; set; } = new();
    }

    public sealed class ItemCachePayload
    {
        public int ItemsLangVersion { get; set; }
        public string ItemsSha256 { get; set; } = "";
        public List<ItemCacheRow> Entries { get; set; } = new();
        public Dictionary<string, string> TypeNames { get; set; } = new();
    }

    public sealed class ItemCacheRow
    {
        public int ItemId { get; set; }
        public string Nombre { get; set; } = "";
        public int Level { get; set; }
        public int TypeId { get; set; }
        public string Category { get; set; } = "";
        public int GfxId { get; set; }
    }

    public static string MonsterCachePath(string directory, int monstersVersion, string mobsFingerprint) =>
        Path.Combine(directory, $"monsters_v{monstersVersion}_{Sanitize(mobsFingerprint)}.json");

    public static string ItemCachePath(string directory, int itemsVersion, string itemsSha) =>
        Path.Combine(directory, $"items_v{itemsVersion}_{Sanitize(itemsSha)}.json");

    public static MonsterCachePayload? TryReadMonsters(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<MonsterCachePayload>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static ItemCachePayload? TryReadItems(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ItemCachePayload>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void WriteMonsters(string path, MonsterCachePayload payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static void WriteItems(string path, ItemCachePayload payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static void InvalidateAll(string? directory = null)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory!;
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try { File.Delete(f); } catch { /* ignore */ }
        }
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "x";
        var take = Math.Min(16, s.Length);
        var span = s.AsSpan(0, take);
        Span<char> buf = stackalloc char[take];
        for (var i = 0; i < take; i++)
        {
            var c = span[i];
            buf[i] = char.IsLetterOrDigit(c) ? c : 'x';
        }
        return new string(buf);
    }
}
