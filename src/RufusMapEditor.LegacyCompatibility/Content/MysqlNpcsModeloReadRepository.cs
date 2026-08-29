using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// READ-ONLY MySQL access to estaticos.npcs_modelo (CONT.2).
/// Only SELECT MAX(id). Never writes.
/// </summary>
public sealed class MysqlNpcsModeloReadRepository : INpcsModeloReadRepository
{
    private readonly string _cs;
    private readonly string _schemaName;
    private readonly string _tableName;

    public MysqlNpcsModeloReadRepository(DatabaseSettings settings, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schemaName = string.IsNullOrWhiteSpace(settings.Database)
            ? NpcsModeloColumns.DefaultDatabase
            : settings.Database.Trim();
        _tableName = NpcsModeloColumns.DefaultTable;
    }

    public async Task<int> GetMaxIdAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(
            $"SELECT MAX(`{NpcsModeloColumns.Id}`) FROM `{_schemaName}`.`{_tableName}`",
            conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null or DBNull)
            return 0;
        return Convert.ToInt32(result);
    }
}
