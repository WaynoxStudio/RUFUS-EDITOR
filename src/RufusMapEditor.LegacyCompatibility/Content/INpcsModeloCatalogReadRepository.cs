using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class NpcCatalogEntry
{
    public required int Id { get; init; }
    public required string Nombre { get; init; }
    public string Display => string.IsNullOrWhiteSpace(Nombre) ? $"#{Id}" : $"{Nombre}  #{Id}";
}

public interface INpcsModeloCatalogReadRepository
{
    Task<IReadOnlyList<NpcCatalogEntry>> SearchAsync(string? query, int take = 80, CancellationToken ct = default);
}

public sealed class FixedNpcsModeloCatalogReadRepository : INpcsModeloCatalogReadRepository
{
    private readonly IReadOnlyList<NpcCatalogEntry> _rows;

    public FixedNpcsModeloCatalogReadRepository(IEnumerable<NpcCatalogEntry> rows) =>
        _rows = rows.ToList();

    public Task<IReadOnlyList<NpcCatalogEntry>> SearchAsync(string? query, int take = 80, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        IEnumerable<NpcCatalogEntry> src = _rows;
        if (q.Length > 0)
        {
            if (int.TryParse(q, out var id))
                src = src.Where(n => n.Id == id || n.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase));
            else
                src = src.Where(n => n.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<NpcCatalogEntry>>(src.Take(Math.Max(1, take)).ToList());
    }
}

/// <summary>ADMIN.UI.4B — READ ONLY search on npcs_modelo (id + nombre).</summary>
public sealed class MysqlNpcsModeloCatalogReadRepository : INpcsModeloCatalogReadRepository
{
    private readonly DatabaseSettings _settings;
    private readonly string _password;
    private readonly string _schema;

    public MysqlNpcsModeloCatalogReadRepository(DatabaseSettings settings, string plainPassword)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _password = plainPassword ?? "";
        _schema = string.IsNullOrWhiteSpace(settings.Database) ? "estaticos" : settings.Database.Trim();
    }

    public async Task<IReadOnlyList<NpcCatalogEntry>> SearchAsync(string? query, int take = 80, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        await using var conn = new MySqlConnection(_settings.BuildConnectionString(_password));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var q = (query ?? "").Trim();
        string sql;
        if (q.Length == 0)
        {
            sql = $@"SELECT `{NpcsModeloColumns.Id}`,`{NpcsModeloColumns.Nombre}`
FROM `{_schema}`.`{NpcsModeloColumns.DefaultTable}` ORDER BY `{NpcsModeloColumns.Id}` LIMIT @take";
        }
        else if (int.TryParse(q, out _))
        {
            sql = $@"SELECT `{NpcsModeloColumns.Id}`,`{NpcsModeloColumns.Nombre}`
FROM `{_schema}`.`{NpcsModeloColumns.DefaultTable}`
WHERE `{NpcsModeloColumns.Id}`=@id OR `{NpcsModeloColumns.Nombre}` LIKE @like
ORDER BY `{NpcsModeloColumns.Id}` LIMIT @take";
        }
        else
        {
            sql = $@"SELECT `{NpcsModeloColumns.Id}`,`{NpcsModeloColumns.Nombre}`
FROM `{_schema}`.`{NpcsModeloColumns.DefaultTable}`
WHERE `{NpcsModeloColumns.Nombre}` LIKE @like
ORDER BY `{NpcsModeloColumns.Id}` LIMIT @take";
        }

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@take", take);
        if (int.TryParse(q, out var id))
            cmd.Parameters.AddWithValue("@id", id);
        if (q.Length > 0)
            cmd.Parameters.AddWithValue("@like", "%" + q.Replace("%", "\\%").Replace("_", "\\_") + "%");

        var list = new List<NpcCatalogEntry>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new NpcCatalogEntry
            {
                Id = rd.GetInt32(0),
                Nombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
            });
        }

        return list;
    }
}
