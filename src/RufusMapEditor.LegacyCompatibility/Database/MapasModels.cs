namespace RufusMapEditor.LegacyCompatibility.Database;

public sealed class MapasRow
{
    public int Id { get; init; }
    public string Fecha { get; init; } = "";
    public int Ancho { get; init; }
    public int Alto { get; init; }
    public int BgId { get; init; }
    public int MusicId { get; init; }
    public int AmbienteId { get; init; }
    public int OutDoor { get; init; }
    public int Capabilities { get; init; }
    public string PosPelea { get; init; } = "";
    public string MapData { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public string? Key { get; init; }
    public string? Mobs { get; init; }
    public int? SubArea { get; init; }
    public int? MaxGrupoMobs { get; init; }
    public int? MaxMobsPorGrupo { get; init; }
    public int? MinNivelGrupoMob { get; init; }
    public int? MaxNivelGrupoMob { get; init; }
    public int? MaxMercantes { get; init; }
    public int? MaxPeleas { get; init; }
    public int? MinMobsPorGrupo { get; init; }
}

public sealed class MapPublishValues
{
    public required int Id { get; init; }
    public required string Fecha { get; init; }
    public required int Ancho { get; init; }
    public required int Alto { get; init; }
    public required int BgId { get; init; }
    public required int MusicId { get; init; }
    public required int AmbienteId { get; init; }
    public required int OutDoor { get; init; }
    public required int Capabilities { get; init; }
    public required string PosPelea { get; init; }
    public required string MapData { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>
    /// Whitelisted columns to SET on UPDATE. Always a subset of <see cref="MapasColumns.Updated"/>.
    /// When null/empty, all Updated columns are written (INSERT / legacy full publish).
    /// </summary>
    public IReadOnlyList<string>? ColumnsToUpdate { get; init; }

    public IReadOnlyList<string> EffectiveUpdateColumns
    {
        get
        {
            if (ColumnsToUpdate is null || ColumnsToUpdate.Count == 0)
                return MapasColumns.Updated;
            var allowed = new HashSet<string>(MapasColumns.Updated, StringComparer.OrdinalIgnoreCase);
            var list = ColumnsToUpdate.Where(c => allowed.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0)
                throw new InvalidOperationException("UPDATE bloqueado: ninguna columna válida en ColumnsToUpdate.");
            return list;
        }
    }
}

public interface IMapasRepository
{
    Task TestConnectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListColumnsAsync(CancellationToken ct = default);
    Task<MapTableSchema> GetTableSchemaAsync(CancellationToken ct = default);
    Task<MapasRow?> TryGetAsync(int mapId, CancellationToken ct = default);
    Task<int> UpdateExistingAsync(MapPublishValues values, CancellationToken ct = default);
    /// <summary>INSERT new row inside a transaction with existence race-check + verify. Returns 1 on success.</summary>
    Task<int> InsertNewAsync(MapInsertPlan plan, CancellationToken ct = default);
}
