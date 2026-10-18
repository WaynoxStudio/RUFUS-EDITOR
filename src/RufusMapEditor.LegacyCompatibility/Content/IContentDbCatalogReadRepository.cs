using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>MISSIONS.DB.READ.1 — READ ONLY row loads for content catalogs.</summary>
public interface IContentDbCatalogReadRepository
{
    Task<IReadOnlyList<DbNpcCatalogRow>> LoadNpcsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DbQuestionCatalogRow>> LoadQuestionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DbMissionCatalogRow>> LoadMissionsAsync(CancellationToken ct = default);
}

public sealed class FixedContentDbCatalogReadRepository : IContentDbCatalogReadRepository
{
    private readonly IReadOnlyList<DbNpcCatalogRow> _npcs;
    private readonly IReadOnlyList<DbQuestionCatalogRow> _questions;
    private readonly IReadOnlyList<DbMissionCatalogRow> _missions;

    public FixedContentDbCatalogReadRepository(
        IEnumerable<DbNpcCatalogRow>? npcs = null,
        IEnumerable<DbQuestionCatalogRow>? questions = null,
        IEnumerable<DbMissionCatalogRow>? missions = null)
    {
        _npcs = npcs?.ToList() ?? [];
        _questions = questions?.ToList() ?? [];
        _missions = missions?.ToList() ?? [];
    }

    public Task<IReadOnlyList<DbNpcCatalogRow>> LoadNpcsAsync(CancellationToken ct = default) =>
        Task.FromResult(_npcs);

    public Task<IReadOnlyList<DbQuestionCatalogRow>> LoadQuestionsAsync(CancellationToken ct = default) =>
        Task.FromResult(_questions);

    public Task<IReadOnlyList<DbMissionCatalogRow>> LoadMissionsAsync(CancellationToken ct = default) =>
        Task.FromResult(_missions);
}

public sealed class MysqlContentDbCatalogReadRepository : IContentDbCatalogReadRepository
{
    private readonly string _cs;
    private readonly string _schema;

    public MysqlContentDbCatalogReadRepository(DatabaseSettings settings, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schema = string.IsNullOrWhiteSpace(settings.Database)
            ? NpcsModeloColumns.DefaultDatabase
            : settings.Database.Trim();
    }

    public async Task<IReadOnlyList<DbNpcCatalogRow>> LoadNpcsAsync(CancellationToken ct = default)
    {
        var sql = $@"SELECT `{NpcsModeloColumns.Id}`,`{NpcsModeloColumns.Nombre}`,`{NpcsModeloColumns.Pregunta}`
FROM `{_schema}`.`{NpcsModeloColumns.DefaultTable}` ORDER BY `{NpcsModeloColumns.Id}`";

        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(sql, conn);
        var list = new List<DbNpcCatalogRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new DbNpcCatalogRow
            {
                Id = rd.GetInt32(0),
                Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                Pregunta = rd.IsDBNull(2) ? 0 : rd.GetInt32(2),
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<DbQuestionCatalogRow>> LoadQuestionsAsync(CancellationToken ct = default)
    {
        var sql = $@"SELECT `{NpcPreguntasColumns.Id}`,`{NpcPreguntasColumns.Respuestas}`,
`{NpcPreguntasColumns.Params}`,`{NpcPreguntasColumns.Alternos}`
FROM `{_schema}`.`{NpcPreguntasColumns.DefaultTable}` ORDER BY `{NpcPreguntasColumns.Id}`";

        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(sql, conn);
        var list = new List<DbQuestionCatalogRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new DbQuestionCatalogRow
            {
                Id = rd.GetInt32(0),
                Respuestas = rd.IsDBNull(1) ? "" : rd.GetString(1),
                Params = rd.IsDBNull(2) ? "" : rd.GetString(2),
                Alternos = rd.IsDBNull(3) ? "" : rd.GetString(3),
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<DbMissionCatalogRow>> LoadMissionsAsync(CancellationToken ct = default)
    {
        var sql = $@"SELECT `{MisionesColumns.Id}`,`{MisionesColumns.Nombre}`,`{MisionesColumns.Etapas}`,
`{MisionesColumns.PregDarMision}`,`{MisionesColumns.PregMisCompletada}`,
`{MisionesColumns.PregMisIncompleta}`,`{MisionesColumns.PuedeRepetirse}`
FROM `{_schema}`.`{MisionesColumns.DefaultTable}` ORDER BY `{MisionesColumns.Id}`";

        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(sql, conn);
        var list = new List<DbMissionCatalogRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new DbMissionCatalogRow
            {
                Id = rd.GetInt32(0),
                Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                Etapas = rd.IsDBNull(2) ? "" : rd.GetString(2),
                PregDarMision = rd.IsDBNull(3) ? "" : rd.GetString(3),
                PregMisCompletada = rd.IsDBNull(4) ? "" : rd.GetString(4),
                PregMisIncompleta = rd.IsDBNull(5) ? "" : rd.GetString(5),
                PuedeRepetirse = !rd.IsDBNull(6) && rd.GetBoolean(6),
            });
        }

        return list;
    }
}
