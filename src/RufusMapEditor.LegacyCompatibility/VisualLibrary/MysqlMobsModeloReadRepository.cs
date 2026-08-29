using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.2 — READ-ONLY SELECT from mobs_modelo. Never writes.</summary>
public sealed class MysqlMobsModeloReadRepository : IMobsModeloReadRepository
{
    private readonly string _cs;
    private readonly string _schemaName;
    private readonly string _tableName;

    public MysqlMobsModeloReadRepository(DatabaseSettings settings, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schemaName = string.IsNullOrWhiteSpace(settings.Database)
            ? MobsModeloColumns.DefaultDatabase
            : settings.Database.Trim();
        _tableName = MobsModeloColumns.DefaultTable;
    }

    public async Task<IReadOnlyList<MobsModeloRow>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var sql =
            $"SELECT `{MobsModeloColumns.Id}`, `{MobsModeloColumns.Nombre}`, `{MobsModeloColumns.GfxId}`, `{MobsModeloColumns.Grados}` " +
            $"FROM `{_schemaName}`.`{_tableName}` ORDER BY `{MobsModeloColumns.Id}`";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<MobsModeloRow>(3000);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new MobsModeloRow
            {
                Id = reader.GetInt32(0),
                Nombre = reader.IsDBNull(1) ? "" : reader.GetString(1),
                GfxId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Grados = reader.IsDBNull(3) ? "" : reader.GetString(3),
            });
        }

        return list;
    }
}
