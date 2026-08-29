using System.IO;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.Rufmap;

namespace RufusMapEditor.App.Services;

public sealed class AutosaveMeta
{
    public required string DocumentId { get; init; }
    public string? ProjectPath { get; init; }
    public int MapId { get; init; }
    public string? DisplayName { get; init; }
    public DateTimeOffset SavedUtc { get; init; }
    public bool HadProjectFile { get; init; }
}

public sealed class RecoverableAutosave
{
    public required AutosaveMeta Meta { get; init; }
    public required string AutosavePath { get; init; }
    public required string MetaPath { get; init; }
}

/// <summary>
/// Recovery autosaves live under LocalApplicationData — never overwrite the user's .rufmap.
/// </summary>
public sealed class AutosaveStore
{
    public static string RootDirectory =>
        Path.Combine(AppSettingsStore.SettingsDirectory, "autosave");

    public static int DefaultIntervalSeconds => 120;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void EnsureRoot() => Directory.CreateDirectory(RootDirectory);

    public string AutosavePathFor(string documentId) =>
        Path.Combine(RootDirectory, $"{Sanitize(documentId)}.rufmap.autosave");

    public string MetaPathFor(string documentId) =>
        Path.Combine(RootDirectory, $"{Sanitize(documentId)}.meta.json");

    public void Write(string documentId, string rufmapJson, AutosaveMeta meta)
    {
        EnsureRoot();
        var autoPath = AutosavePathFor(documentId);
        var metaPath = MetaPathFor(documentId);
        var tmp = autoPath + ".tmp";

        File.WriteAllText(tmp, rufmapJson);
        if (new FileInfo(tmp).Length == 0)
        {
            File.Delete(tmp);
            throw new IOException("Autosave temporal vacío.");
        }

        if (File.Exists(autoPath))
            File.Replace(tmp, autoPath, destinationBackupFileName: null);
        else
            File.Move(tmp, autoPath);

        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));
    }

    public void Delete(string documentId)
    {
        TryDelete(AutosavePathFor(documentId));
        TryDelete(MetaPathFor(documentId));
    }

    public IReadOnlyList<RecoverableAutosave> ListRecoverable()
    {
        EnsureRoot();
        var list = new List<RecoverableAutosave>();
        foreach (var metaPath in Directory.EnumerateFiles(RootDirectory, "*.meta.json"))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<AutosaveMeta>(File.ReadAllText(metaPath), JsonOptions);
                if (meta is null) continue;
                var auto = AutosavePathFor(meta.DocumentId);
                if (!File.Exists(auto)) continue;

                // If project file exists and is newer than autosave, skip (obsolete).
                if (!string.IsNullOrWhiteSpace(meta.ProjectPath) && File.Exists(meta.ProjectPath))
                {
                    var projectTime = File.GetLastWriteTimeUtc(meta.ProjectPath);
                    if (projectTime >= meta.SavedUtc.UtcDateTime)
                        continue;
                }

                list.Add(new RecoverableAutosave
                {
                    Meta = meta,
                    AutosavePath = auto,
                    MetaPath = metaPath,
                });
            }
            catch
            {
                // ignore bad entries
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
