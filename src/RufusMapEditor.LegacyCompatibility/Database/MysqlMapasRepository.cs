using System.Globalization;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace RufusMapEditor.LegacyCompatibility.Database;

public sealed class MysqlMapasRepository : IMapasRepository
{
    private readonly string _cs;
    private readonly string _schemaName;
    private readonly string _tableName;
    private readonly string _tableSql;

    public MysqlMapasRepository(DatabaseSettings settings, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schemaName = string.IsNullOrWhiteSpace(settings.Database) ? MapasColumns.DefaultDatabase : settings.Database.Trim();
        _tableName = string.IsNullOrWhiteSpace(settings.Table) ? MapasColumns.DefaultTable : settings.Table.Trim();
        _tableSql = QuoteIdent(_tableName);
    }

    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand("SELECT 1", conn);
        _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListColumnsAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
            conn);
        cmd.Parameters.AddWithValue("@s", _schemaName);
        cmd.Parameters.AddWithValue("@t", _tableName);
        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add(reader.GetString(0));
        return list;
    }

    public async Task<MapTableSchema> GetTableSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(
            """
            SELECT COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT,
                   CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE,
                   COLUMN_KEY, EXTRA, ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """,
            conn);
        cmd.Parameters.AddWithValue("@schema", _schemaName);
        cmd.Parameters.AddWithValue("@table", _tableName);

        var columns = new List<MapColumnSchema>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            columns.Add(new MapColumnSchema
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                ColumnType = reader.GetString(2),
                IsNullable = string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase),
                ColumnDefault = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4), CultureInfo.InvariantCulture),
                CharacterMaximumLength = reader.IsDBNull(5) ? null : Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
                NumericPrecision = reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                NumericScale = reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture),
                ColumnKey = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Extra = reader.IsDBNull(9) ? "" : reader.GetString(9),
                OrdinalPosition = Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture),
            });
        }

        return new MapTableSchema
        {
            SchemaName = _schemaName,
            TableName = _tableName,
            Columns = columns,
        };
    }

    public async Task<MapasRow?> TryGetAsync(int mapId, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql();
        cmd.Parameters.AddWithValue("@id", mapId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return Read(r);
    }

    public async Task<int> UpdateExistingAsync(MapPublishValues values, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int affected;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                var cols = values.EffectiveUpdateColumns;
                var setParts = new List<string>();
                foreach (var col in cols)
                {
                    var param = col switch
                    {
                        MapasColumns.Fecha => "@fecha",
                        MapasColumns.Ancho => "@ancho",
                        MapasColumns.Alto => "@alto",
                        MapasColumns.BgId => "@bg",
                        MapasColumns.MusicId => "@music",
                        MapasColumns.AmbienteId => "@amb",
                        MapasColumns.OutDoor => "@outdoor",
                        MapasColumns.Capabilities => "@caps",
                        MapasColumns.PosPelea => "@pos",
                        MapasColumns.MapData => "@mapData",
                        MapasColumns.X => "@x",
                        MapasColumns.Y => "@y",
                        _ => throw new InvalidOperationException($"Columna no permitida en UPDATE: {col}"),
                    };
                    setParts.Add($"`{col}`={param}");
                }
                cmd.CommandText =
                    $"UPDATE {_tableSql} SET {string.Join(", ", setParts)} WHERE `{MapasColumns.Id}`=@id";
                AddParams(cmd, values);
                affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (affected > 1)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"CRITICAL: UPDATE affected {affected} rows for id={values.Id}.");
            }

            MapasRow verify;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = SelectSql();
                cmd.Parameters.AddWithValue("@id", values.Id);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    throw new InvalidOperationException("Verification failed: row missing after UPDATE.");
                }

                verify = Read(r);
            }

            if (!EqualsPublished(verify, values))
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException("Verification failed: published values do not match SELECT.");
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return affected;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* ignore */ }
            throw;
        }
    }

    public async Task<int> InsertNewAsync(MapInsertPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanInsert)
            throw new InvalidOperationException("INSERT bloqueado: faltan valores para columnas obligatorias.");

        var included = plan.Included.ToList();
        if (included.Count == 0)
            throw new InvalidOperationException("INSERT bloqueado: no hay columnas incluidas.");
        if (included.Select(c => c.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != included.Count)
            throw new InvalidOperationException("INSERT bloqueado: hay columnas duplicadas.");

        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var exists = conn.CreateCommand())
            {
                exists.Transaction = tx;
                exists.CommandText = $"SELECT 1 FROM {_tableSql} WHERE `{MapasColumns.Id}`=@id LIMIT 1 FOR UPDATE";
                exists.Parameters.AddWithValue("@id", plan.EditorValues.Id);
                if (await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
                    throw new InvalidOperationException(
                        $"El mapa {plan.EditorValues.Id} ya existe (carrera de creación). Vuelva a publicar para usar UPDATE.");
            }

            int affected;
            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                var names = included.Select(c => QuoteIdent(c.ColumnName)).ToArray();
                var parameters = included.Select((_, i) => $"@v{i}").ToArray();
                insert.CommandText =
                    $"INSERT INTO {_tableSql} ({string.Join(",", names)}) VALUES ({string.Join(",", parameters)})";
                for (var i = 0; i < included.Count; i++)
                    insert.Parameters.AddWithValue(parameters[i], included[i].Value ?? DBNull.Value);
                affected = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (affected != 1)
                throw new InvalidOperationException(
                    $"INSERT no confirmado: affected_rows={affected}; se esperaba exactamente 1.");

            MapasRow verify;
            await using (var select = conn.CreateCommand())
            {
                select.Transaction = tx;
                select.CommandText = SelectSql();
                select.Parameters.AddWithValue("@id", plan.EditorValues.Id);
                await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new InvalidOperationException("Verification failed: row missing after INSERT.");
                verify = Read(reader);
            }

            if (!EqualsPublished(verify, plan.EditorValues))
                throw new InvalidOperationException("Verification failed: INSERT values do not match SELECT.");

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return affected;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* preserve original */ }
            throw;
        }
    }

    private string SelectSql() => $"""
        SELECT `{MapasColumns.Id}`,`{MapasColumns.Fecha}`,`{MapasColumns.Ancho}`,`{MapasColumns.Alto}`,
               `{MapasColumns.BgId}`,`{MapasColumns.MusicId}`,`{MapasColumns.AmbienteId}`,`{MapasColumns.OutDoor}`,
               `{MapasColumns.Capabilities}`,`{MapasColumns.PosPelea}`,`{MapasColumns.MapData}`,
               `{MapasColumns.X}`,`{MapasColumns.Y}`,
               `{MapasColumns.Key}`,`{MapasColumns.Mobs}`,`{MapasColumns.SubArea}`,
               `{MapasColumns.MaxGrupoMobs}`,`{MapasColumns.MaxMobsPorGrupo}`,
               `{MapasColumns.MinNivelGrupoMob}`,`{MapasColumns.MaxNivelGrupoMob}`,
               `{MapasColumns.MaxMercantes}`,`{MapasColumns.MaxPeleas}`,`{MapasColumns.MinMobsPorGrupo}`
        FROM {_tableSql}
        WHERE `{MapasColumns.Id}`=@id
        LIMIT 1
        """;

    private static void AddParams(MySqlCommand cmd, MapPublishValues v)
    {
        cmd.Parameters.AddWithValue("@fecha", v.Fecha);
        cmd.Parameters.AddWithValue("@ancho", v.Ancho);
        cmd.Parameters.AddWithValue("@alto", v.Alto);
        cmd.Parameters.AddWithValue("@bg", v.BgId);
        cmd.Parameters.AddWithValue("@music", v.MusicId);
        cmd.Parameters.AddWithValue("@amb", v.AmbienteId);
        cmd.Parameters.AddWithValue("@outdoor", v.OutDoor);
        cmd.Parameters.AddWithValue("@caps", v.Capabilities);
        cmd.Parameters.AddWithValue("@pos", v.PosPelea);
        cmd.Parameters.AddWithValue("@mapData", v.MapData);
        cmd.Parameters.AddWithValue("@x", v.X);
        cmd.Parameters.AddWithValue("@y", v.Y);
        cmd.Parameters.AddWithValue("@id", v.Id);
    }

    private static bool EqualsPublished(MapasRow row, MapPublishValues v) =>
        string.Equals(row.Fecha, v.Fecha, StringComparison.Ordinal)
        && row.Ancho == v.Ancho && row.Alto == v.Alto
        && row.BgId == v.BgId && row.MusicId == v.MusicId && row.AmbienteId == v.AmbienteId
        && row.OutDoor == v.OutDoor && row.Capabilities == v.Capabilities
        && string.Equals(row.PosPelea, v.PosPelea, StringComparison.Ordinal)
        && string.Equals(row.MapData, v.MapData, StringComparison.Ordinal)
        && row.X == v.X && row.Y == v.Y;

    private static MapasRow Read(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Fecha = r.IsDBNull(1) ? "" : Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture) ?? "",
        Ancho = r.GetInt32(2),
        Alto = r.GetInt32(3),
        BgId = r.GetInt32(4),
        MusicId = r.GetInt32(5),
        AmbienteId = r.GetInt32(6),
        OutDoor = Convert.ToInt32(r.GetValue(7), CultureInfo.InvariantCulture),
        Capabilities = r.GetInt32(8),
        PosPelea = r.IsDBNull(9) ? "" : r.GetString(9),
        MapData = r.IsDBNull(10) ? "" : r.GetString(10),
        X = r.GetInt32(11),
        Y = r.GetInt32(12),
        Key = r.IsDBNull(13) ? null : Convert.ToString(r.GetValue(13), CultureInfo.InvariantCulture),
        Mobs = r.IsDBNull(14) ? null : Convert.ToString(r.GetValue(14), CultureInfo.InvariantCulture),
        SubArea = r.IsDBNull(15) ? null : Convert.ToInt32(r.GetValue(15), CultureInfo.InvariantCulture),
        MaxGrupoMobs = r.IsDBNull(16) ? null : Convert.ToInt32(r.GetValue(16), CultureInfo.InvariantCulture),
        MaxMobsPorGrupo = r.IsDBNull(17) ? null : Convert.ToInt32(r.GetValue(17), CultureInfo.InvariantCulture),
        MinNivelGrupoMob = r.IsDBNull(18) ? null : Convert.ToInt32(r.GetValue(18), CultureInfo.InvariantCulture),
        MaxNivelGrupoMob = r.IsDBNull(19) ? null : Convert.ToInt32(r.GetValue(19), CultureInfo.InvariantCulture),
        MaxMercantes = r.IsDBNull(20) ? null : Convert.ToInt32(r.GetValue(20), CultureInfo.InvariantCulture),
        MaxPeleas = r.IsDBNull(21) ? null : Convert.ToInt32(r.GetValue(21), CultureInfo.InvariantCulture),
        MinMobsPorGrupo = r.IsDBNull(22) ? null : Convert.ToInt32(r.GetValue(22), CultureInfo.InvariantCulture),
    };

    private static string QuoteIdent(string name)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_]+$"))
            throw new ArgumentException("Invalid MySQL identifier.");
        return "`" + name + "`";
    }
}
