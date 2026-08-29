using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class AreaCatalogEntry
{
    public required int Id { get; init; }
    public required string Nombre { get; init; }
    public int SuperArea { get; init; }
    public string Display => string.IsNullOrWhiteSpace(Nombre) ? $"#{Id}" : $"{Nombre}  #{Id}";
}

public interface IAreasReadRepository
{
    Task<IReadOnlyList<AreaCatalogEntry>> SearchAsync(string? query, int take = 80, CancellationToken ct = default);
    Task<AreaCatalogEntry?> GetByIdAsync(int id, CancellationToken ct = default);
}

public sealed class FixedAreasReadRepository : IAreasReadRepository
{
    private readonly IReadOnlyList<AreaCatalogEntry> _rows;

    public FixedAreasReadRepository(IEnumerable<AreaCatalogEntry> rows) =>
        _rows = rows.ToList();

    public Task<IReadOnlyList<AreaCatalogEntry>> SearchAsync(string? query, int take = 80, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        IEnumerable<AreaCatalogEntry> src = _rows;
        if (q.Length > 0)
        {
            if (int.TryParse(q, out var id))
                src = src.Where(a => a.Id == id || a.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase));
            else
                src = src.Where(a => a.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<AreaCatalogEntry>>(src.Take(Math.Max(1, take)).ToList());
    }

    public Task<AreaCatalogEntry?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(_rows.FirstOrDefault(a => a.Id == id));
}

public sealed class MysqlAreasReadRepository : IAreasReadRepository
{
    private readonly DatabaseSettings _settings;
    private readonly string _password;
    private readonly string _schema;

    public MysqlAreasReadRepository(DatabaseSettings settings, string plainPassword)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _password = plainPassword ?? "";
        _schema = string.IsNullOrWhiteSpace(settings.Database) ? "estaticos" : settings.Database.Trim();
    }

    public async Task<IReadOnlyList<AreaCatalogEntry>> SearchAsync(string? query, int take = 80, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        await using var conn = new MySqlConnection(_settings.BuildConnectionString(_password));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var q = (query ?? "").Trim();
        string sql;
        if (q.Length == 0)
        {
            sql = $@"SELECT `id`,`nombre`,`superarea` FROM `{_schema}`.`areas` ORDER BY `id` LIMIT @take";
        }
        else if (int.TryParse(q, out _))
        {
            sql = $@"SELECT `id`,`nombre`,`superarea` FROM `{_schema}`.`areas`
WHERE `id`=@id OR `nombre` LIKE @like ORDER BY `id` LIMIT @take";
        }
        else
        {
            sql = $@"SELECT `id`,`nombre`,`superarea` FROM `{_schema}`.`areas`
WHERE `nombre` LIKE @like ORDER BY `id` LIMIT @take";
        }

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@take", take);
        if (int.TryParse(q, out var id))
            cmd.Parameters.AddWithValue("@id", id);
        if (q.Length > 0)
            cmd.Parameters.AddWithValue("@like", "%" + q.Replace("%", "\\%").Replace("_", "\\_") + "%");

        var list = new List<AreaCatalogEntry>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new AreaCatalogEntry
            {
                Id = rd.GetInt32(0),
                Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                SuperArea = rd.IsDBNull(2) ? 0 : rd.GetInt32(2),
            });
        }

        return list;
    }

    public async Task<AreaCatalogEntry?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_settings.BuildConnectionString(_password));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var sql = $@"SELECT `id`,`nombre`,`superarea` FROM `{_schema}`.`areas` WHERE `id`=@id LIMIT 1";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return new AreaCatalogEntry
        {
            Id = rd.GetInt32(0),
            Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
            SuperArea = rd.IsDBNull(2) ? 0 : rd.GetInt32(2),
        };
    }
}
