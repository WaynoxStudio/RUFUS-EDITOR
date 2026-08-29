using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

public interface IMisionEtapasReadRepository
{
    Task<int> GetMaxIdAsync(CancellationToken ct = default);
}

public interface IMisionObjetivosReadRepository
{
    Task<int> GetMaxIdAsync(CancellationToken ct = default);
}

public sealed class FixedMisionEtapasReadRepository : IMisionEtapasReadRepository
{
    private readonly int _maxId;
    public FixedMisionEtapasReadRepository(int maxId) => _maxId = maxId;
    public Task<int> GetMaxIdAsync(CancellationToken ct = default) => Task.FromResult(_maxId);
}

public sealed class FixedMisionObjetivosReadRepository : IMisionObjetivosReadRepository
{
    private readonly int _maxId;
    public FixedMisionObjetivosReadRepository(int maxId) => _maxId = maxId;
    public Task<int> GetMaxIdAsync(CancellationToken ct = default) => Task.FromResult(_maxId);
}

public sealed class MysqlMisionEtapasReadRepository : IMisionEtapasReadRepository
{
    private readonly string _cs;
    private readonly string _schemaName;

    public MysqlMisionEtapasReadRepository(DatabaseSettings settings, string plainPassword)
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
            $"SELECT MAX(`{MisionEtapasColumns.Id}`) FROM `{_schemaName}`.`{MisionEtapasColumns.DefaultTable}`",
            conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null or DBNull) return 0;
        return Convert.ToInt32(result);
    }
}

public sealed class MysqlMisionObjetivosReadRepository : IMisionObjetivosReadRepository
{
    private readonly string _cs;
    private readonly string _schemaName;

    public MysqlMisionObjetivosReadRepository(DatabaseSettings settings, string plainPassword)
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
            $"SELECT MAX(`{MisionObjetivosColumns.Id}`) FROM `{_schemaName}`.`{MisionObjetivosColumns.DefaultTable}`",
            conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null or DBNull) return 0;
        return Convert.ToInt32(result);
    }
}
