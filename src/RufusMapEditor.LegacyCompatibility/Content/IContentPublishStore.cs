namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>BD surface for CONT.5 content publish. Tests use <see cref="InMemoryContentPublishStore"/>.</summary>
public interface IContentPublishStore
{
    Task<IReadOnlyList<ContentTableEngineInfo>> GetEnginesAsync(CancellationToken ct = default);
    Task<bool> CanLockTablesAsync(CancellationToken ct = default);
    Task<ContentPublishMaxSnapshot> ReadMaxIdsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<int>> FindExistingIdsAsync(string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default);

    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);

    Task LockTablesWriteAsync(IReadOnlyList<string> tables, CancellationToken ct = default);
    Task UnlockTablesAsync(CancellationToken ct = default);

    Task InsertNpcAsync(NpcModeloInsertRow row, CancellationToken ct = default);
    Task InsertUbicacionAsync(NpcUbicacionInsertRow row, CancellationToken ct = default);
    Task InsertPreguntaAsync(NpcPreguntaInsertRow row, CancellationToken ct = default);
    Task InsertRespuestaActionAsync(NpcRespuestaInsertRow row, CancellationToken ct = default);
    Task InsertMisionAsync(MisionInsertRow row, CancellationToken ct = default);
    Task InsertEtapaAsync(MisionEtapaInsertRow row, CancellationToken ct = default);
    Task InsertObjetivoAsync(MisionObjetivoInsertRow row, CancellationToken ct = default);

    Task<int> CountByIdsAsync(string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default);
    Task<int> CountUbicacionesByNpcIdsAsync(IReadOnlyList<int> npcIds, CancellationToken ct = default);
    Task<int> CountRespuestaRowsByLogicalIdsAsync(IReadOnlyList<int> responseIds, CancellationToken ct = default);

    Task DeleteByIdsAsync(string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default);
    Task DeleteUbicacionesByNpcIdsAsync(IReadOnlyList<int> npcIds, CancellationToken ct = default);
}

public static class ContentPublishTables
{
    public static readonly string[] All =
    {
        NpcsModeloColumns.DefaultTable,
        NpcsUbicacionColumns.DefaultTable,
        NpcPreguntasColumns.DefaultTable,
        NpcRespuestasColumns.DefaultTable,
        MisionesColumns.DefaultTable,
        MisionEtapasColumns.DefaultTable,
        MisionObjetivosColumns.DefaultTable,
    };
}
