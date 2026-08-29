using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class NpcGfxUsageRow
{
    public required int GfxId { get; init; }
    public required string Nombre { get; init; }
}

public interface INpcsGfxUsageReadRepository
{
    Task<IReadOnlyList<NpcGfxUsageRow>> GetAllGfxUsageAsync(CancellationToken ct = default);
}

public sealed class FixedNpcsGfxUsageReadRepository : INpcsGfxUsageReadRepository
{
    private readonly IReadOnlyList<NpcGfxUsageRow> _rows;

    public FixedNpcsGfxUsageReadRepository(IEnumerable<NpcGfxUsageRow> rows) =>
        _rows = rows.ToList();

    public Task<IReadOnlyList<NpcGfxUsageRow>> GetAllGfxUsageAsync(CancellationToken ct = default) =>
        Task.FromResult(_rows);
}

/// <summary>ADMIN.UI.4B.2A.3C — READ ONLY gfx usage from npcs_modelo.</summary>
public sealed class MysqlNpcsGfxUsageReadRepository : INpcsGfxUsageReadRepository
{
    private readonly DatabaseSettings _settings;
    private readonly string _password;
    private readonly string _schema;

    public MysqlNpcsGfxUsageReadRepository(DatabaseSettings settings, string plainPassword)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _password = plainPassword ?? "";
        _schema = string.IsNullOrWhiteSpace(settings.Database) ? NpcsModeloColumns.DefaultDatabase : settings.Database.Trim();
    }

    public async Task<IReadOnlyList<NpcGfxUsageRow>> GetAllGfxUsageAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_settings.BuildConnectionString(_password));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var sql =
            $"SELECT `{NpcsModeloColumns.GfxId}`, `{NpcsModeloColumns.Nombre}` " +
            $"FROM `{_schema}`.`{NpcsModeloColumns.DefaultTable}` " +
            $"ORDER BY `{NpcsModeloColumns.GfxId}`, `{NpcsModeloColumns.Id}`";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<NpcGfxUsageRow>(1200);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new NpcGfxUsageRow
            {
                GfxId = rd.GetInt32(0),
                Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
            });
        }

        return list;
    }
}
