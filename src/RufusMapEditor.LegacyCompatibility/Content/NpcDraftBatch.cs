namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// In-memory NPC draft batch with provisional IDs = MAX(db)+1 consecutive.
/// Does not fill historical gaps. No BD/SFTP writes.
/// </summary>
public sealed class NpcDraftBatch
{
    private readonly List<NpcsModeloDraft> _drafts = new();
    private int _dbMaxId;

    public IReadOnlyList<NpcsModeloDraft> Drafts => _drafts;

    /// <summary>Last MAX(id) read from npcs_modelo (read-only).</summary>
    public int DbMaxId => _dbMaxId;

    /// <summary>Next provisional id that would be assigned (MAX(db, drafts)+1).</summary>
    public int NextProvisionalId => ComputeNextId();

    public void SetDbMaxId(int maxId)
    {
        if (maxId < 0)
            throw new ArgumentOutOfRangeException(nameof(maxId));
        _dbMaxId = maxId;
    }

    public NpcsModeloDraft CreateNew()
    {
        var id = ComputeNextId();
        EnsureUnique(id);
        var draft = NpcsModeloDraft.CreateWithDefaults(id);
        _drafts.Add(draft);
        return draft;
    }

    public NpcsModeloDraft Duplicate(NpcsModeloDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_drafts.Contains(source) && _drafts.All(d => d.Id != source.Id))
            throw new InvalidOperationException("El NPC a duplicar no pertenece al lote.");

        var copy = source.CloneData();
        copy.Id = ComputeNextId();
        copy.PublishedBd = false;
        EnsureUnique(copy.Id);
        // Locations were deep-copied; npc column always resolves from copy.Id (never original).
        _drafts.Add(copy);
        return copy;
    }

    /// <summary>Adds a local ubicacion; Map/Cell stay unset (0) until the user fills them.</summary>
    public NpcLocationDraft AddLocation(NpcsModeloDraft npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (!_drafts.Contains(npc) && _drafts.All(d => d.Id != npc.Id))
            throw new InvalidOperationException("El NPC no pertenece al lote.");

        var loc = new NpcLocationDraft
        {
            MapId = 0,
            CellId = 0,
            Orientation = 0,
            Condition = "",
        };
        npc.Locations.Add(loc);
        return loc;
    }

    public bool RemoveLocation(NpcsModeloDraft npc, NpcLocationDraft location)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(location);
        return npc.Locations.Remove(location);
    }

    public bool Remove(NpcsModeloDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        // Locations live on the draft; removing the NPC drops its ubicaciones with it.
        return _drafts.Remove(draft) || _drafts.RemoveAll(d => d.Id == draft.Id) > 0;
    }

    public bool RemoveById(int id)
    {
        var idx = _drafts.FindIndex(d => d.Id == id);
        if (idx < 0)
            return false;
        _drafts.RemoveAt(idx);
        return true;
    }

    public NpcsModeloDraft? FindById(int id) =>
        _drafts.FirstOrDefault(d => d.Id == id);

    /// <summary>Import a draft keeping its provisional id (for workspace load/save).</summary>
    public void ImportPreservingId(NpcsModeloDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        EnsureUnique(draft.Id);
        _drafts.Add(draft.CloneData()); // includes Locations for CONT.5 transactional publish prep
    }

    /// <summary>
    /// Flatten for future CONT.5 publish: each row pairs resolved npc id with location fields.
    /// No INSERT here.
    /// </summary>
    public IReadOnlyList<(int NpcId, NpcLocationDraft Location)> EnumerateLocationsForPublish()
    {
        var list = new List<(int, NpcLocationDraft)>();
        foreach (var npc in _drafts)
        {
            foreach (var loc in npc.Locations)
                list.Add((npc.Id, loc));
        }
        return list;
    }

    public void Clear() => _drafts.Clear();

    public bool HasDuplicateIds()
    {
        var seen = new HashSet<int>();
        foreach (var d in _drafts)
        {
            if (!seen.Add(d.Id))
                return true;
        }
        return false;
    }

    public IReadOnlyList<int> ProvisionalIds => _drafts.Select(d => d.Id).ToList();

    private int ComputeNextId()
    {
        var maxDraft = _drafts.Count == 0 ? 0 : _drafts.Max(d => d.Id);
        var floor = Math.Max(_dbMaxId, maxDraft);
        return floor + 1;
    }

    private void EnsureUnique(int id)
    {
        if (_drafts.Any(d => d.Id == id))
            throw new InvalidOperationException($"ID provisional duplicado en el lote: {id}");
    }
}
