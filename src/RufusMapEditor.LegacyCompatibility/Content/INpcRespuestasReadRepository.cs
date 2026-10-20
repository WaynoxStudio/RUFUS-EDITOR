using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>READ-ONLY SELECT MAX(id) FROM npc_respuestas. Never reads orden. Never writes.</summary>
public interface INpcRespuestasReadRepository
{
    Task<int> GetMaxIdAsync(CancellationToken ct = default);
}

public sealed class FixedNpcRespuestasReadRepository : INpcRespuestasReadRepository
{
    private readonly int _maxId;
    public FixedNpcRespuestasReadRepository(int maxId) => _maxId = maxId;
    public Task<int> GetMaxIdAsync(CancellationToken ct = default) => Task.FromResult(_maxId);
}

public sealed class MysqlNpcRespuestasReadRepository : INpcRespuestasReadRepository
{
    private readonly string _cs;
    private readonly string _schemaName;

    public MysqlNpcRespuestasReadRepository(DatabaseSettings settings, string plainPassword)
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
            $"SELECT MAX(`{NpcRespuestasColumns.Id}`) FROM `{_schemaName}`.`{NpcRespuestasColumns.DefaultTable}`",
            conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null or DBNull) return 0;
        return Convert.ToInt32(result);
    }
}
