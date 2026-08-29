using System.IO;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.World;

namespace RufusMapEditor.App.Services;

public sealed class WorldAutosaveMeta
{
    public required string WorldId { get; init; }
    public string? WorldPath { get; init; }
    public string? DisplayName { get; init; }
    public DateTimeOffset SavedUtc { get; init; }
    public bool HadWorldFile { get; init; }
}

public sealed class RecoverableWorldAutosave
{
    public required WorldAutosaveMeta Meta { get; init; }
    public required string AutosavePath { get; init; }
    public required string MetaPath { get; init; }
}

public sealed class WorldAutosaveStore
{
    public static string RootDirectory =>
        Path.Combine(AppSettingsStore.SettingsDirectory, "world-autosave");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void EnsureRoot() => Directory.CreateDirectory(RootDirectory);

    public string AutosavePathFor(string worldId) =>
        Path.Combine(RootDirectory, $"{Sanitize(worldId)}.rufworld.autosave");

    public string MetaPathFor(string worldId) =>
        Path.Combine(RootDirectory, $"{Sanitize(worldId)}.meta.json");

    public void Write(string worldId, string rufworldJson, WorldAutosaveMeta meta)
    {
        EnsureRoot();
        var autoPath = AutosavePathFor(worldId);
        var metaPath = MetaPathFor(worldId);
        var tmp = autoPath + ".tmp";
        File.WriteAllText(tmp, rufworldJson);
        if (new FileInfo(tmp).Length == 0)
        {
            File.Delete(tmp);
            throw new IOException("Autosave mundo temporal vacío.");
        }

        if (File.Exists(autoPath))
            File.Replace(tmp, autoPath, destinationBackupFileName: null);
        else
            File.Move(tmp, autoPath);

        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));
    }

    public void Delete(string worldId)
    {
        TryDelete(AutosavePathFor(worldId));
        TryDelete(MetaPathFor(worldId));
    }

    public IReadOnlyList<RecoverableWorldAutosave> ListRecoverable()
    {
        EnsureRoot();
        var list = new List<RecoverableWorldAutosave>();
        foreach (var metaPath in Directory.EnumerateFiles(RootDirectory, "*.meta.json"))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<WorldAutosaveMeta>(File.ReadAllText(metaPath), JsonOptions);
                if (meta is null) continue;
                var auto = AutosavePathFor(meta.WorldId);
                if (!File.Exists(auto)) continue;

                if (!string.IsNullOrWhiteSpace(meta.WorldPath) && File.Exists(meta.WorldPath))
                {
                    var projectTime = File.GetLastWriteTimeUtc(meta.WorldPath);
                    if (projectTime >= meta.SavedUtc.UtcDateTime)
                        continue;
                }

                list.Add(new RecoverableWorldAutosave
                {
                    Meta = meta,
                    AutosavePath = auto,
                    MetaPath = metaPath,
                });
            }
            catch
            {
                /* ignore */
            }
        }

        return list.OrderByDescending(x => x.Meta.SavedUtc).ToList();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }

    private static string Sanitize(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            id = id.Replace(c, '_');
        return id;
    }
}
