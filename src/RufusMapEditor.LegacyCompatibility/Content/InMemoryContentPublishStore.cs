using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>In-memory publish store for CONT.5 unit tests. Never touches production MySQL.</summary>
public sealed class InMemoryContentPublishStore : IContentPublishStore
{
    private readonly object _gate = new();
    private ContentPublishMaxSnapshot _maxes;
    private readonly Dictionary<string, string> _engines;
    private bool _allowLocks = true;
    private bool _failNextInsert;
    private string? _failTable;

    public int InsertCallCount { get; private set; }
    public int DeleteCallCount { get; private set; }
    public bool WasLocked { get; private set; }
    public bool WasTransactional { get; private set; }

    public Dictionary<int, NpcModeloInsertRow> Npcs { get; } = new();
    public List<NpcUbicacionInsertRow> Locations { get; } = new();
    public Dictionary<int, NpcPreguntaInsertRow> Questions { get; } = new();
    public List<NpcRespuestaInsertRow> Responses { get; } = new();
    public Dictionary<int, MisionInsertRow> Missions { get; } = new();
    public Dictionary<int, MisionEtapaInsertRow> Stages { get; } = new();
    public Dictionary<int, MisionObjetivoInsertRow> Objectives { get; } = new();

    /// <summary>Pre-seeded existing IDs that collide with reservations.</summary>
    public HashSet<int> ExistingNpcIds { get; } = new();
    public HashSet<int> ExistingQuestionIds { get; } = new();
    public HashSet<int> ExistingResponseIds { get; } = new();
    public HashSet<int> ExistingQuestIds { get; } = new();
    public HashSet<int> ExistingStageIds { get; } = new();
    public HashSet<int> ExistingObjectiveIds { get; } = new();

    public InMemoryContentPublishStore(ContentPublishMaxSnapshot? maxes = null, bool allMyisam = true)
    {
        _maxes = maxes ?? new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20061,
            NpcPreguntas = 20023,
            NpcRespuestas = 90001,
            Misiones = 100003,
            MisionEtapas = 5500,
            MisionObjetivos = 4214,
        };
        var engine = allMyisam ? "MyISAM" : "InnoDB";
        _engines = ContentPublishTables.All.ToDictionary(t => t, _ => engine, StringComparer.OrdinalIgnoreCase);
    }

    public void SetMaxes(ContentPublishMaxSnapshot maxes) => _maxes = maxes;
    public void SetAllowLocks(bool allow) => _allowLocks = allow;
    public void SetEngine(string table, string engine) => _engines[table] = engine;
    public void FailNextInsertOn(string table)
    {
        _failNextInsert = true;
        _failTable = table;
    }

    public Task<IReadOnlyList<ContentTableEngineInfo>> GetEnginesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ContentTableEngineInfo> list = _engines
            .Select(kv => new ContentTableEngineInfo { Table = kv.Key, Engine = kv.Value })
            .ToList();
        return Task.FromResult(list);
    }

    public Task<bool> CanLockTablesAsync(CancellationToken ct = default) =>
        Task.FromResult(_allowLocks);

    public Task<ContentPublishMaxSnapshot> ReadMaxIdsAsync(CancellationToken ct = default)
    {
        // Live MAX from base snapshot + rows inserted via this store.
        // Existing* sets are collision seeds only (do not raise MAX).
        lock (_gate)
        {
            return Task.FromResult(new ContentPublishMaxSnapshot
            {
                NpcsModelo = Math.Max(_maxes.NpcsModelo, Npcs.Keys.DefaultIfEmpty(0).Max()),
                NpcPreguntas = Math.Max(_maxes.NpcPreguntas, Questions.Keys.DefaultIfEmpty(0).Max()),
                NpcRespuestas = Math.Max(_maxes.NpcRespuestas, Responses.Select(r => r.Id).DefaultIfEmpty(0).Max()),
                Misiones = Math.Max(_maxes.Misiones, Missions.Keys.DefaultIfEmpty(0).Max()),
                MisionEtapas = Math.Max(_maxes.MisionEtapas, Stages.Keys.DefaultIfEmpty(0).Max()),
                MisionObjetivos = Math.Max(_maxes.MisionObjetivos, Objectives.Keys.DefaultIfEmpty(0).Max()),
            });
        }
    }

    public Task<IReadOnlyList<int>> FindExistingIdsAsync(string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var set = TableExisting(table);
            IReadOnlyList<int> found = ids.Where(id => set.Contains(id) || TableHas(table, id)).ToList();
            return Task.FromResult(found);
        }
    }

    private HashSet<int> TableExisting(string table) => table switch
    {
        NpcsModeloColumns.DefaultTable => ExistingNpcIds,
        NpcPreguntasColumns.DefaultTable => ExistingQuestionIds,
        NpcRespuestasColumns.DefaultTable => ExistingResponseIds,
        MisionesColumns.DefaultTable => ExistingQuestIds,
        MisionEtapasColumns.DefaultTable => ExistingStageIds,
        MisionObjetivosColumns.DefaultTable => ExistingObjectiveIds,
        _ => new HashSet<int>(),
    };

    private bool TableHas(string table, int id) => table switch
    {
        NpcsModeloColumns.DefaultTable => Npcs.ContainsKey(id),
        NpcPreguntasColumns.DefaultTable => Questions.ContainsKey(id),
        NpcRespuestasColumns.DefaultTable => Responses.Any(r => r.Id == id),
        MisionesColumns.DefaultTable => Missions.ContainsKey(id),
        MisionEtapasColumns.DefaultTable => Stages.ContainsKey(id),
        MisionObjetivosColumns.DefaultTable => Objectives.ContainsKey(id),
        _ => false,
    };

    public Task BeginTransactionAsync(CancellationToken ct = default)
    {
        WasTransactional = true;
        return Task.CompletedTask;
    }

    public Task CommitTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task RollbackTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task LockTablesWriteAsync(IReadOnlyList<string> tables, CancellationToken ct = default)
    {
        if (!_allowLocks)
            throw new InvalidOperationException("LOCK TABLES denegado (simulado).");
        WasLocked = true;
        return Task.CompletedTask;
    }

    public Task UnlockTablesAsync(CancellationToken ct = default) => Task.CompletedTask;

    private void ThrowIfFail(string table)
    {
        if (_failNextInsert && string.Equals(_failTable, table, StringComparison.OrdinalIgnoreCase))
        {
            _failNextInsert = false;
            throw new InvalidOperationException($"INSERT simulado fallido en {table}");
        }
    }

    public Task InsertNpcAsync(NpcModeloInsertRow row, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ThrowIfFail(NpcsModeloColumns.DefaultTable);
            InsertCallCount++;
            Npcs[row.Id] = row;
        }
        return Task.CompletedTask;
    }

    public Task InsertUbicacionAsync(NpcUbicacionInsertRow row, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ThrowIfFail(NpcsUbicacionColumns.DefaultTable);
            InsertCallCount++;
            Locations.Add(row);
        }
        return Task.CompletedTask;
    }

    public Task InsertPreguntaAsync(NpcPreguntaInsertRow row, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ThrowIfFail(NpcPreguntasColumns.DefaultTable);
            InsertCallCount++;
            Questions[row.Id] = row;
        }
        return Task.CompletedTask;
    }

    public Task InsertRespuestaActionAsync(NpcRespuestaInsertRow row, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ThrowIfFail(NpcRespuestasColumns.DefaultTable);
            InsertCallCount++;
            Responses.Add(row);
        }
        return Task.CompletedTask;
    }

    public Task InsertMisionAsync(MisionInsertRow row, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ThrowIfFail(MisionesColumns.DefaultTable);
            InsertCallCount++;
            Missions[row.Id] = row;
        }
        return Task.CompletedTask;
    }

    public Task InsertEtapaAsync(MisionEtapaInsertRow row, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ThrowIfFail(MisionEtapasColumns.DefaultTable);
            InsertCallCount++;
            Stages[row.Id] = row;
        }
        return Task.CompletedTask;
    }

    public Task InsertObjetivoAsync(MisionObjetivoInsertRow row, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ThrowIfFail(MisionObjetivosColumns.DefaultTable);
            InsertCallCount++;
            Objectives[row.Id] = row;
        }
        return Task.CompletedTask;
    }

    public Task<int> CountByIdsAsync(string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(ids.Count(id => TableHas(table, id)));
        }
    }

    public Task<int> CountUbicacionesByNpcIdsAsync(IReadOnlyList<int> npcIds, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(Locations.Count(l => npcIds.Contains(l.Npc)));
    }

    public Task<int> CountRespuestaRowsByLogicalIdsAsync(IReadOnlyList<int> responseIds, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(Responses.Count(r => responseIds.Contains(r.Id)));
    }

    public Task DeleteByIdsAsync(string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        lock (_gate)
        {
            DeleteCallCount++;
            switch (table)
            {
                case NpcsModeloColumns.DefaultTable:
                    foreach (var id in ids) Npcs.Remove(id);
                    break;
                case NpcPreguntasColumns.DefaultTable:
                    foreach (var id in ids) Questions.Remove(id);
                    break;
                case NpcRespuestasColumns.DefaultTable:
                    Responses.RemoveAll(r => ids.Contains(r.Id));
                    break;
                case MisionesColumns.DefaultTable:
                    foreach (var id in ids) Missions.Remove(id);
                    break;
                case MisionEtapasColumns.DefaultTable:
                    foreach (var id in ids) Stages.Remove(id);
                    break;
                case MisionObjetivosColumns.DefaultTable:
                    foreach (var id in ids) Objectives.Remove(id);
                    break;
            }
        }
        return Task.CompletedTask;
    }

    public Task DeleteUbicacionesByNpcIdsAsync(IReadOnlyList<int> npcIds, CancellationToken ct = default)
    {
        lock (_gate)
        {
            DeleteCallCount++;
            Locations.RemoveAll(l => npcIds.Contains(l.Npc));
        }
        return Task.CompletedTask;
    }
}
