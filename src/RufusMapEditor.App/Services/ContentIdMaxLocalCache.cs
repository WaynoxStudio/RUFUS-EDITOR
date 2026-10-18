using System.IO;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Disk cache for content MAX(id) snapshot — instant Contenido UI while MySQL refreshes.
/// </summary>
public static class ContentIdMaxLocalCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string CachePath =>
        Path.Combine(AppSettingsStore.SettingsDirectory, "content-id-max-cache.json");

    public static string BuildDbKey(DatabaseSettings db) =>
        $"{db.Host?.Trim().ToLowerInvariant()}:{db.Port}:{db.Database?.Trim().ToLowerInvariant()}";

    public static bool TryLoad(DatabaseSettings db, out ContentIdMaxCatalog catalog)
    {
        catalog = null!;
        try
        {
            if (!File.Exists(CachePath))
                return false;

            var json = File.ReadAllText(CachePath);
            var payload = JsonSerializer.Deserialize<Payload>(json);
            if (payload is null)
                return false;

            var key = BuildDbKey(db);
            if (!string.Equals(payload.DbKey, key, StringComparison.Ordinal))
                return false;

            catalog = new ContentIdMaxCatalog
            {
                NpcMax = payload.NpcMax,
                QuestionMax = payload.QuestionMax,
                ResponseMax = payload.ResponseMax,
                QuestMax = payload.QuestMax,
                StageMax = payload.StageMax,
                ObjectiveMax = payload.ObjectiveMax,
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(DatabaseSettings db, ContentIdMaxCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        try
        {
            Directory.CreateDirectory(AppSettingsStore.SettingsDirectory);
            var payload = new Payload
            {
                DbKey = BuildDbKey(db),
                SavedAtUtc = DateTime.UtcNow,
                NpcMax = catalog.NpcMax,
                QuestionMax = catalog.QuestionMax,
                ResponseMax = catalog.ResponseMax,
                QuestMax = catalog.QuestMax,
                StageMax = catalog.StageMax,
                ObjectiveMax = catalog.ObjectiveMax,
            };
            File.WriteAllText(CachePath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch
        {
            // Best-effort only.
        }
    }

    private sealed class Payload
    {
        public string DbKey { get; set; } = "";
        public DateTime SavedAtUtc { get; set; }
        public int NpcMax { get; set; }
        public int QuestionMax { get; set; }
        public int ResponseMax { get; set; }
        public int QuestMax { get; set; }
        public int StageMax { get; set; }
        public int ObjectiveMax { get; set; }
    }
}
