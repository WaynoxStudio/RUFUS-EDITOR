using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

public interface INpcPreguntasReadRepository
{
    Task<int> GetMaxIdAsync(CancellationToken ct = default);
}

public sealed class FixedNpcPreguntasReadRepository : INpcPreguntasReadRepository
{
    private readonly int _maxId;
    public FixedNpcPreguntasReadRepository(int maxId) => _maxId = maxId;
    public Task<int> GetMaxIdAsync(CancellationToken ct = default) => Task.FromResult(_maxId);
}

/// <summary>READ-ONLY SELECT MAX(id) FROM npc_preguntas. Never writes.</summary>
public sealed class MysqlNpcPreguntasReadRepository : INpcPreguntasReadRepository
{
    private readonly string _cs;
    private readonly string _schemaName;

    public MysqlNpcPreguntasReadRepository(DatabaseSettings settings, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schemaName = string.IsNullOrWhiteSpace(settings.Database)
            ? NpcsModeloColumns.DefaultDatabase
            : settings.Database.Trim();
    }

    public async Task<int> GetMaxIdAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(
            $"SELECT MAX(`{NpcPreguntasColumns.Id}`) FROM `{_schemaName}`.`{NpcPreguntasColumns.DefaultTable}`",
            conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null or DBNull) return 0;
        return Convert.ToInt32(result);
    }
}
