using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.4 — MySQL mobs_fix READ + parameterized REPLACE (7 columns only).</summary>
public sealed class MysqlMobsFixRepository : IMobsFixRepository
{
    private readonly string _cs;
    private readonly string _schemaName;
    private readonly string _tableName;

    public MysqlMobsFixRepository(DatabaseSettings settings, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schemaName = string.IsNullOrWhiteSpace(settings.Database)
            ? MobsFixColumns.DefaultDatabase
            : settings.Database.Trim();
        _tableName = MobsFixColumns.DefaultTable;
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand("SELECT 1", conn);
        _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    public async Task ValidateSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new MySqlCommand(
                         """
                         SELECT COLUMN_NAME
                         FROM information_schema.COLUMNS
                         WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                         """,
                         conn))
        {
            cmd.Parameters.AddWithValue("@schema", _schemaName);
            cmd.Parameters.AddWithValue("@table", _tableName);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                cols.Add(reader.GetString(0));
        }

        if (cols.Count == 0)
            throw new MobsFixSchemaException(
                $"Tabla `{_schemaName}`.`{_tableName}` no encontrada.");

        foreach (var required in MobsFixColumns.RequiredSchemaColumns)
        {
            if (!cols.Contains(required))
                throw new MobsFixSchemaException(
                    $"Columna esperada ausente en mobs_fix: `{required}`.");
        }

        var pk = new List<string>();
        await using (var cmd = new MySqlCommand(
                         """
                         SELECT COLUMN_NAME
                         FROM information_schema.KEY_COLUMN_USAGE
                         WHERE TABLE_SCHEMA = @schema
                           AND TABLE_NAME = @table
                           AND CONSTRAINT_NAME = 'PRIMARY'
                         ORDER BY ORDINAL_POSITION
                         """,
                         conn))
        {
            cmd.Parameters.AddWithValue("@schema", _schemaName);
            cmd.Parameters.AddWithValue("@table", _tableName);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                pk.Add(reader.GetString(0));
        }

        if (pk.Count != 2
            || !string.Equals(pk[0], MobsFixColumns.Mapa, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pk[1], MobsFixColumns.Celda, StringComparison.OrdinalIgnoreCase))
        {
            throw new MobsFixSchemaException(
                $"PK de mobs_fix debe ser (mapa, celda). Actual: ({string.Join(", ", pk)}).");
        }
    }

    public async Task<IReadOnlyList<MobsFixRow>> GetByMapaAsync(int mapa, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(
            $"""
             SELECT `{MobsFixColumns.Mapa}`, `{MobsFixColumns.Celda}`, `{MobsFixColumns.Mobs}`,
                    `{MobsFixColumns.Tipo}`, `{MobsFixColumns.Condicion}`, `{MobsFixColumns.SegundosRespawn}`,
                    `{MobsFixColumns.Descripcion}`, `{MobsFixColumns.Sala}`, `{MobsFixColumns.Movible}`,
                    `{MobsFixColumns.Oleadas}`, `{MobsFixColumns.Id}`
             FROM `{_schemaName}`.`{_tableName}`
             WHERE `{MobsFixColumns.Mapa}` = @mapa
             ORDER BY `{MobsFixColumns.Celda}`
             """,
            conn);
        cmd.Parameters.AddWithValue("@mapa", mapa);
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<MobsFixRow?> GetByMapaCeldaAsync(int mapa, int celda, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(
            $"""
             SELECT `{MobsFixColumns.Mapa}`, `{MobsFixColumns.Celda}`, `{MobsFixColumns.Mobs}`,
                    `{MobsFixColumns.Tipo}`, `{MobsFixColumns.Condicion}`, `{MobsFixColumns.SegundosRespawn}`,
                    `{MobsFixColumns.Descripcion}`, `{MobsFixColumns.Sala}`, `{MobsFixColumns.Movible}`,
                    `{MobsFixColumns.Oleadas}`, `{MobsFixColumns.Id}`
             FROM `{_schemaName}`.`{_tableName}`
             WHERE `{MobsFixColumns.Mapa}` = @mapa AND `{MobsFixColumns.Celda}` = @celda
             LIMIT 1
             """,
            conn);
        cmd.Parameters.AddWithValue("@mapa", mapa);
        cmd.Parameters.AddWithValue("@celda", celda);
        var rows = await ReadAllAsync(cmd, ct).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task ReplaceAsync(MobsFixPublishRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        // Exactly the 7 columns the game server uses — never Sala/movible/oleadas/id.
        await using var cmd = new MySqlCommand(
            $"""
             REPLACE INTO `{_schemaName}`.`{_tableName}`
             (`{MobsFixColumns.Mapa}`, `{MobsFixColumns.Celda}`, `{MobsFixColumns.Mobs}`,
              `{MobsFixColumns.Tipo}`, `{MobsFixColumns.Condicion}`, `{MobsFixColumns.SegundosRespawn}`,
              `{MobsFixColumns.Descripcion}`)
             VALUES (@mapa, @celda, @mobs, @tipo, @condicion, @segundos, @descripcion)
             """,
            conn);
        cmd.Parameters.AddWithValue("@mapa", request.Mapa);
        cmd.Parameters.AddWithValue("@celda", request.Celda);
        cmd.Parameters.AddWithValue("@mobs", request.Mobs);
        cmd.Parameters.AddWithValue("@tipo", request.Tipo);
        cmd.Parameters.AddWithValue("@condicion", request.Condicion ?? "");
        cmd.Parameters.AddWithValue("@segundos", request.SegundosRespawn);
        cmd.Parameters.AddWithValue("@descripcion", request.Descripcion ?? "");
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> MobModeloExistsAsync(int mobId, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(
            $"""
             SELECT 1 FROM `{_schemaName}`.`{MobsModeloColumns.DefaultTable}`
             WHERE `{MobsModeloColumns.Id}` = @id LIMIT 1
             """,
            conn);
        cmd.Parameters.AddWithValue("@id", mobId);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null && o is not DBNull;
    }

    private static async Task<IReadOnlyList<MobsFixRow>> ReadAllAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var list = new List<MobsFixRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var mobs = reader.IsDBNull(2) ? "" : reader.GetString(2);
            list.Add(new MobsFixRow
            {
                Mapa = reader.GetInt32(0),
                Celda = reader.GetInt32(1),
                Mobs = mobs,
                Tipo = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                Condicion = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SegundosRespawn = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                Descripcion = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Sala = reader.IsDBNull(7) ? null : reader.GetString(7),
                Movible = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Oleadas = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                Id = reader.IsDBNull(10) ? null : reader.GetInt64(10),
                HasLegacyOrUnrecognizedMobsFormat = !MobsFixGroupString.IsStrictFormat(mobs),
            });
        }

        return list;
    }
}
