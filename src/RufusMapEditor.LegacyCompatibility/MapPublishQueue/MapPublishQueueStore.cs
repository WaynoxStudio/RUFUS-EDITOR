using System.Security.Cryptography;
using System.Text.Json;

namespace RufusMapEditor.LegacyCompatibility.MapPublishQueue;

/// <summary>
/// Portable publish queue under Library/PublishQueue/queue.json.
/// Survives restart; no absolute machine paths stored.
/// </summary>
public sealed class MapPublishQueueStore
{
    public const string FolderName = "PublishQueue";
    public const string FileName = "queue.json";
    public const string MapsFolderName = "Maps";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();
    private MapPublishQueueDocument _doc = new();
    private string? _libraryRoot;

    public string? LibraryRoot => _libraryRoot;

    public IReadOnlyList<MapPublishQueueItem> Items
    {
        get
        {
            lock (_gate)
                return _doc.Items.OrderBy(i => i.MapId).ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _doc.Items.Count;
        }
    }

    public static string GetQueueDirectory(string libraryRoot) =>
        Path.Combine(Path.GetFullPath(libraryRoot), FolderName);

    public static string GetQueuePath(string libraryRoot) =>
        Path.Combine(GetQueueDirectory(libraryRoot), FileName);

    public static string GetOfficialRufmapPath(string libraryRoot, int mapId) =>
        Path.Combine(
            Path.GetFullPath(libraryRoot),
            MapsFolderName,
            mapId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            mapId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".rufmap");

    public static bool HasOfficialSave(string libraryRoot, int mapId) =>
        File.Exists(GetOfficialRufmapPath(libraryRoot, mapId));

    /// <summary>Bind to Library root and load (or create empty).</summary>
    public void ConfigureLibraryRoot(string? libraryRoot)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot))
            {
                _libraryRoot = null;
                _doc = new MapPublishQueueDocument();
                return;
            }

            _libraryRoot = Path.GetFullPath(libraryRoot.Trim());
            _doc = LoadOrCreate(_libraryRoot);
        }
    }

    public bool TryGet(int mapId, out MapPublishQueueItem? item)
    {
        lock (_gate)
        {
            item = _doc.Items.FirstOrDefault(i => i.MapId == mapId);
            return item is not null;
        }
    }

    /// <summary>Add or replace entry for MapId. Returns true if newly added.</summary>
    public bool Upsert(MapPublishQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.MapId <= 0)
            throw new ArgumentOutOfRangeException(nameof(item), "MapId must be > 0.");

        lock (_gate)
        {
            EnsureConfigured();
            var existing = _doc.Items.FindIndex(i => i.MapId == item.MapId);
            if (existing >= 0)
            {
                _doc.Items[existing] = item;
                PersistUnlocked();
                return false;
            }

            _doc.Items.Add(item);
            PersistUnlocked();
            return true;
        }
    }

    public bool Remove(int mapId)
    {
        lock (_gate)
        {
            EnsureConfigured();
            var n = _doc.Items.RemoveAll(i => i.MapId == mapId);
            if (n > 0)
                PersistUnlocked();
            return n > 0;
        }
    }

    public void RemoveMany(IEnumerable<int> mapIds)
    {
        var set = mapIds.ToHashSet();
        if (set.Count == 0) return;
        lock (_gate)
        {
            EnsureConfigured();
            var before = _doc.Items.Count;
            _doc.Items.RemoveAll(i => set.Contains(i.MapId));
            if (_doc.Items.Count != before)
                PersistUnlocked();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            EnsureConfigured();
            if (_doc.Items.Count == 0) return;
            _doc.Items.Clear();
            PersistUnlocked();
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            if (_libraryRoot is null)
            {
                _doc = new MapPublishQueueDocument();
                return;
            }

            _doc = LoadOrCreate(_libraryRoot);
        }
    }

    /// <summary>SHA-256 of official .rufmap bytes, or null if missing.</summary>
    public static string? TryComputeRufmapSha256(string libraryRoot, int mapId)
    {
        var path = GetOfficialRufmapPath(libraryRoot, mapId);
        if (!File.Exists(path))
            return null;
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static MapPublishQueueItemStatus EvaluateStatus(
        MapPublishQueueItem item,
        string libraryRoot,
        bool hasUnsavedChangesForMap)
    {
        if (item.MapId <= 0)
            return MapPublishQueueItemStatus.MissingLocalSave;
        if (hasUnsavedChangesForMap)
            return MapPublishQueueItemStatus.UnsavedChanges;
        if (!HasOfficialSave(libraryRoot, item.MapId))
            return MapPublishQueueItemStatus.MissingLocalSave;

        var sha = TryComputeRufmapSha256(libraryRoot, item.MapId);
        if (sha is null)
            return MapPublishQueueItemStatus.MissingLocalSave;
        if (!string.Equals(sha, item.RufmapSha256, StringComparison.OrdinalIgnoreCase))
            return MapPublishQueueItemStatus.ModifiedAfterQueued;

        if (!item.SubAreaDefined || !item.WorldCoordinatesSet)
            return MapPublishQueueItemStatus.MissingPublishFields;

        return MapPublishQueueItemStatus.Ready;
    }

    public static string StatusLabel(MapPublishQueueItemStatus status, MapPublishQueueItem? item = null)
    {
        if (status == MapPublishQueueItemStatus.MissingPublishFields && item is not null)
        {
            var parts = new List<string>();
            if (!item.SubAreaDefined)
                parts.Add("Falta SubArea (sa)");
            if (!item.WorldCoordinatesSet)
                parts.Add("Falta X/Y");
            return parts.Count == 0
                ? "⚠ Falta información"
                : "⚠ " + string.Join(" · ", parts);
        }

        return status switch
        {
            MapPublishQueueItemStatus.Ready => "✓ Preparado",
            MapPublishQueueItemStatus.ModifiedAfterQueued => "⚠ Modificado después de añadir",
            MapPublishQueueItemStatus.UnsavedChanges => "⚠ Cambios sin guardar",
            MapPublishQueueItemStatus.MissingLocalSave => "⚠ Sin guardado local",
            MapPublishQueueItemStatus.MissingPublishFields => "⚠ Falta información",
            _ => status.ToString(),
        };
    }

    /// <summary>Publish-time blockers for one item (empty = ok).</summary>
    public static IReadOnlyList<string> GetPublishBlockers(
        MapPublishQueueItem item,
        string libraryRoot,
        bool hasUnsavedChangesForMap)
    {
        var list = new List<string>();
        var status = EvaluateStatus(item, libraryRoot, hasUnsavedChangesForMap);
        if (status == MapPublishQueueItemStatus.UnsavedChanges)
            list.Add("cambios sin guardar");
        if (status == MapPublishQueueItemStatus.MissingLocalSave)
            list.Add("sin guardado local");
        if (status == MapPublishQueueItemStatus.ModifiedAfterQueued)
            list.Add("modificado después de añadir (guarda o actualiza en cola)");
        if (!item.SubAreaDefined)
            list.Add("falta SubArea (sa)");
        if (!item.WorldCoordinatesSet)
            list.Add("falta X/Y");
        if (item.Ep < 0)
            list.Add("EP inválido");
        return list;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_libraryRoot))
            throw new InvalidOperationException("Cola de publicación: Library no configurada.");
    }

    private void PersistUnlocked()
    {
        var root = _libraryRoot ?? throw new InvalidOperationException("Library root missing.");
        var dir = GetQueueDirectory(root);
        Directory.CreateDirectory(dir);
        var path = GetQueuePath(root);
        var json = JsonSerializer.Serialize(_doc, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, path, overwrite: true);
        try { File.Delete(tmp); } catch { /* ignore */ }
    }

    private static MapPublishQueueDocument LoadOrCreate(string libraryRoot)
    {
        try
        {
            var path = GetQueuePath(libraryRoot);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var doc = JsonSerializer.Deserialize<MapPublishQueueDocument>(json, JsonOptions);
                    if (doc is not null)
                    {
                        doc.Items ??= new List<MapPublishQueueItem>();
                        doc.Items = doc.Items
                            .GroupBy(i => i.MapId)
                            .Select(g => g.Last())
                            .OrderBy(i => i.MapId)
                            .ToList();
                        // Pre-1.3 queues always required sa at enqueue; migrate once.
                        if (doc.Version < 2)
                        {
                            foreach (var item in doc.Items)
                                item.SubAreaDefined = true;
                            doc.Version = 2;
                        }

                        return doc;
                    }
                }
            }
        }
        catch
        {
            // Corrupt → fresh
        }

        return new MapPublishQueueDocument();
    }
}
