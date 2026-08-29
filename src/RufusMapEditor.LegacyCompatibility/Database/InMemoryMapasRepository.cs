using System.Collections.Concurrent;
using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Database;

public sealed class InMemoryMapasRepository : IMapasRepository
{
    private readonly ConcurrentDictionary<int, MapasRow> _rows = new();
    private readonly List<string> _columns;
    private List<MapColumnSchema> _schemaColumns;
    private bool _failNextUpdate;
    private bool _failNextInsert;
    private int? _raceInsertId;
    private int? _nextInsertAffected;
    private bool _corruptNextInsert;

    public InMemoryMapasRepository(IEnumerable<string>? columns = null)
    {
        _columns = (columns ?? MapasColumns.Required.Concat(MapasColumns.Preserved))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _schemaColumns = BuildDefaultSchema(_columns);
    }

    public int UpdateCount { get; private set; }
    public int InsertCount { get; private set; }
    public MapPublishValues? LastUpdate { get; private set; }
    public MapInsertPlan? LastInsert { get; private set; }

    public void Seed(MapasRow row) => _rows[row.Id] = Clone(row);

    public void FailNextUpdate() => _failNextUpdate = true;

    public void FailNextInsert() => _failNextInsert = true;

    public void NextInsertAffectedRows(int affected) => _nextInsertAffected = affected;

    public void CorruptNextInsertForVerification() => _corruptNextInsert = true;

    /// <summary>Simulates another session inserting this id between race-check and insert.</summary>
    public void RaceInsertAsExisting(int mapId) => _raceInsertId = mapId;

    public void RemoveColumn(string name)
    {
        _columns.RemoveAll(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        _schemaColumns.RemoveAll(c => string.Equals(c.ColumnName, name, StringComparison.OrdinalIgnoreCase));
    }

    public void SetSchema(IEnumerable<MapColumnSchema> columns)
    {
        _schemaColumns = columns.ToList();
        _columns.Clear();
        _columns.AddRange(_schemaColumns.Select(c => c.ColumnName));
    }

    public Task TestConnectionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListColumnsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(_columns);

    public Task<MapTableSchema> GetTableSchemaAsync(CancellationToken ct = default) =>
        Task.FromResult(new MapTableSchema
        {
            SchemaName = MapasColumns.DefaultDatabase,
            TableName = MapasColumns.DefaultTable,
            Columns = _schemaColumns.ToList(),
        });

    public Task<MapasRow?> TryGetAsync(int mapId, CancellationToken ct = default)
    {
        _rows.TryGetValue(mapId, out var row);
        return Task.FromResult(row is null ? null : Clone(row));
    }

    public Task<int> UpdateExistingAsync(MapPublishValues values, CancellationToken ct = default)
    {
        if (_failNextUpdate)
        {
            _failNextUpdate = false;
            throw new InvalidOperationException("Simulated update failure.");
        }

        if (!_rows.TryGetValue(values.Id, out var prev))
            return Task.FromResult(0);

        UpdateCount++;
        LastUpdate = values;
        _rows[values.Id] = ApplyEditor(prev, values);
        return Task.FromResult(1);
    }

    public Task<int> InsertNewAsync(MapInsertPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_failNextInsert)
        {
            _failNextInsert = false;
            throw new InvalidOperationException("Simulated insert failure.");
        }

        var id = plan.EditorValues.Id;
        if (_raceInsertId == id || _rows.ContainsKey(id))
            throw new InvalidOperationException("El mapa ya existe. Vuelve a publicar para comparar cambios.");

        if (!plan.CanInsert)
            throw new InvalidOperationException("INSERT bloqueado: faltan defaults.");

        if (_nextInsertAffected is int forced)
        {
            _nextInsertAffected = null;
            return Task.FromResult(forced);
        }

        LastInsert = plan;
        var row = BuildRowFromPlan(plan);
        if (_corruptNextInsert)
        {
            _corruptNextInsert = false;
            throw new InvalidOperationException("Verification failed: INSERT values do not match SELECT.");
        }
        _rows[id] = row;
        InsertCount++;
        return Task.FromResult(1);
    }

    private static MapasRow ApplyEditor(MapasRow prev, MapPublishValues values)
    {
        var cols = new HashSet<string>(values.EffectiveUpdateColumns, StringComparer.OrdinalIgnoreCase);
        bool Has(string c) => cols.Contains(c);
        return new MapasRow
        {
            Id = values.Id,
            Fecha = Has(MapasColumns.Fecha) ? values.Fecha : prev.Fecha,
            Ancho = Has(MapasColumns.Ancho) ? values.Ancho : prev.Ancho,
            Alto = Has(MapasColumns.Alto) ? values.Alto : prev.Alto,
            BgId = Has(MapasColumns.BgId) ? values.BgId : prev.BgId,
            MusicId = Has(MapasColumns.MusicId) ? values.MusicId : prev.MusicId,
            AmbienteId = Has(MapasColumns.AmbienteId) ? values.AmbienteId : prev.AmbienteId,
            OutDoor = Has(MapasColumns.OutDoor) ? values.OutDoor : prev.OutDoor,
            Capabilities = Has(MapasColumns.Capabilities) ? values.Capabilities : prev.Capabilities,
            PosPelea = Has(MapasColumns.PosPelea) ? values.PosPelea : prev.PosPelea,
            MapData = Has(MapasColumns.MapData) ? values.MapData : prev.MapData,
            X = Has(MapasColumns.X) ? values.X : prev.X,
            Y = Has(MapasColumns.Y) ? values.Y : prev.Y,
            Key = prev.Key,
            Mobs = prev.Mobs,
            SubArea = prev.SubArea,
            MaxGrupoMobs = prev.MaxGrupoMobs,
            MaxMobsPorGrupo = prev.MaxMobsPorGrupo,
            MinNivelGrupoMob = prev.MinNivelGrupoMob,
            MaxNivelGrupoMob = prev.MaxNivelGrupoMob,
            MaxMercantes = prev.MaxMercantes,
            MaxPeleas = prev.MaxPeleas,
            MinMobsPorGrupo = prev.MinMobsPorGrupo,
        };
    }

    private MapasRow BuildRowFromPlan(MapInsertPlan plan)
    {
        var e = plan.EditorValues;
        string? GetStr(string col)
        {
            var c = plan.Columns.FirstOrDefault(x =>
                string.Equals(x.ColumnName, col, StringComparison.OrdinalIgnoreCase));
            if (c is null || c.Source is InsertColumnSource.DatabaseDefault)
                return _schemaColumns.FirstOrDefault(x =>
                    string.Equals(x.ColumnName, col, StringComparison.OrdinalIgnoreCase))?.ColumnDefault;
            if (c.Source is InsertColumnSource.ExplicitNull)
                return null;
            return Convert.ToString(c.Value, CultureInfo.InvariantCulture);
        }

        int? GetInt(string col)
        {
            var c = plan.Columns.FirstOrDefault(x =>
                string.Equals(x.ColumnName, col, StringComparison.OrdinalIgnoreCase));
            if (c is null || c.Source is InsertColumnSource.DatabaseDefault)
            {
                var raw = _schemaColumns.FirstOrDefault(x =>
                    string.Equals(x.ColumnName, col, StringComparison.OrdinalIgnoreCase))?.ColumnDefault;
                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }
            if (c.Source is InsertColumnSource.ExplicitNull || c.Value is null)
                return null;
            return Convert.ToInt32(c.Value, CultureInfo.InvariantCulture);
        }

        return new MapasRow
        {
            Id = e.Id,
            Fecha = e.Fecha,
            Ancho = e.Ancho,
            Alto = e.Alto,
            BgId = e.BgId,
            MusicId = e.MusicId,
            AmbienteId = e.AmbienteId,
            OutDoor = e.OutDoor,
            Capabilities = e.Capabilities,
            PosPelea = e.PosPelea,
            MapData = e.MapData,
            X = e.X,
            Y = e.Y,
            Key = GetStr(MapasColumns.Key),
            Mobs = GetStr(MapasColumns.Mobs),
            SubArea = GetInt(MapasColumns.SubArea),
            MaxGrupoMobs = GetInt(MapasColumns.MaxGrupoMobs),
            MaxMobsPorGrupo = GetInt(MapasColumns.MaxMobsPorGrupo),
            MinNivelGrupoMob = GetInt(MapasColumns.MinNivelGrupoMob),
            MaxNivelGrupoMob = GetInt(MapasColumns.MaxNivelGrupoMob),
            MaxMercantes = GetInt(MapasColumns.MaxMercantes),
            MaxPeleas = GetInt(MapasColumns.MaxPeleas),
            MinMobsPorGrupo = GetInt(MapasColumns.MinMobsPorGrupo),
        };
    }

    /// <summary>
    /// Test helper: preserved columns get MySQL-like defaults so INSERT can omit them.
    /// </summary>
    public static IReadOnlyList<MapColumnSchema> SchemaWithDbDefaultsForPreserved()
    {
        var list = new List<MapColumnSchema>();
        var ord = 1;
        foreach (var name in MapasColumns.Required)
        {
            list.Add(new MapColumnSchema
            {
                ColumnName = name,
                DataType = name is MapasColumns.Fecha or MapasColumns.PosPelea or MapasColumns.MapData ? "text" : "int",
                ColumnType = "int",
                IsNullable = false,
                ColumnDefault = name == MapasColumns.Id ? null : null,
                ColumnKey = name == MapasColumns.Id ? "PRI" : "",
                Extra = "",
                OrdinalPosition = ord++,
            });
        }

        foreach (var name in MapasColumns.Preserved)
        {
            list.Add(new MapColumnSchema
            {
                ColumnName = name,
                DataType = name is MapasColumns.Key or MapasColumns.Mobs ? "text" : "int",
                ColumnType = name is MapasColumns.Key or MapasColumns.Mobs ? "text" : "int",
                IsNullable = false,
                ColumnDefault = name switch
                {
                    MapasColumns.Key => "",
                    MapasColumns.Mobs => "",
                    MapasColumns.SubArea => "0",
                    MapasColumns.MaxGrupoMobs => "4",
                    MapasColumns.MaxMobsPorGrupo => "8",
                    MapasColumns.MinNivelGrupoMob => "0",
                    MapasColumns.MaxNivelGrupoMob => "0",
                    MapasColumns.MaxMercantes => "5",
                    MapasColumns.MaxPeleas => "99",
                    MapasColumns.MinMobsPorGrupo => "1",
                    _ => "0",
                },
                ColumnKey = "",
                Extra = "",
                OrdinalPosition = ord++,
            });
        }

        return list;
    }

    /// <summary>
    /// Production-like schema: preserved columns NOT NULL without MySQL DEFAULT (HOTFIX 10B.2).
    /// </summary>
    public static IReadOnlyList<MapColumnSchema> SchemaWithoutDbDefaultsForPreserved()
    {
        var list = new List<MapColumnSchema>();
        var ord = 1;
        foreach (var name in MapasColumns.Required)
        {
            list.Add(new MapColumnSchema
            {
                ColumnName = name,
                DataType = name is MapasColumns.Fecha or MapasColumns.PosPelea or MapasColumns.MapData ? "text" : "int",
                ColumnType = "int",
                IsNullable = false,
                ColumnDefault = null,
                ColumnKey = name == MapasColumns.Id ? "PRI" : "",
                Extra = "",
                OrdinalPosition = ord++,
            });
        }

        foreach (var name in MapasColumns.Preserved)
        {
            list.Add(new MapColumnSchema
            {
                ColumnName = name,
                DataType = name is MapasColumns.Key or MapasColumns.Mobs ? "text" : "int",
                ColumnType = name is MapasColumns.Key or MapasColumns.Mobs ? "text" : "int",
                IsNullable = false,
                ColumnDefault = null,
                ColumnKey = "",
                Extra = "",
                OrdinalPosition = ord++,
            });
        }

        return list;
    }

    private static List<MapColumnSchema> BuildDefaultSchema(IEnumerable<string> columns) =>
        columns.Select((name, i) => new MapColumnSchema
        {
            ColumnName = name,
            DataType = "int",
            ColumnType = "int",
            IsNullable = MapasColumns.Preserved.Contains(name, StringComparer.OrdinalIgnoreCase),
            ColumnDefault = MapasColumns.Preserved.Contains(name, StringComparer.OrdinalIgnoreCase) ? "0" : null,
            ColumnKey = name == MapasColumns.Id ? "PRI" : "",
            Extra = "",
            OrdinalPosition = i + 1,
        }).ToList();

    private static MapasRow Clone(MapasRow r) => new()
    {
        Id = r.Id,
        Fecha = r.Fecha,
        Ancho = r.Ancho,
        Alto = r.Alto,
        BgId = r.BgId,
        MusicId = r.MusicId,
        AmbienteId = r.AmbienteId,
        OutDoor = r.OutDoor,
        Capabilities = r.Capabilities,
        PosPelea = r.PosPelea,
        MapData = r.MapData,
        X = r.X,
        Y = r.Y,
        Key = r.Key,
        Mobs = r.Mobs,
        SubArea = r.SubArea,
        MaxGrupoMobs = r.MaxGrupoMobs,
        MaxMobsPorGrupo = r.MaxMobsPorGrupo,
        MinNivelGrupoMob = r.MinNivelGrupoMob,
        MaxNivelGrupoMob = r.MaxNivelGrupoMob,
        MaxMercantes = r.MaxMercantes,
        MaxPeleas = r.MaxPeleas,
        MinMobsPorGrupo = r.MinMobsPorGrupo,
    };

}
