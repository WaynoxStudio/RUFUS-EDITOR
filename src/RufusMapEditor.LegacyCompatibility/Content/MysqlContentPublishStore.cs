using System.Globalization;
using System.Text;
using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>MySQL CONT.5 writer. Parameterized INSERT only. No REPLACE/IGNORE/UPDATE of existing.</summary>
public sealed class MysqlContentPublishStore : IContentPublishStore, IAsyncDisposable
{
    private readonly MySqlConnection _conn;
    private readonly string _schema;
    private MySqlTransaction? _tx;
    private bool _locked;

    public MysqlContentPublishStore(DatabaseSettings settings, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _schema = string.IsNullOrWhiteSpace(settings.Database)
            ? NpcsModeloColumns.DefaultDatabase
            : settings.Database.Trim();
        _conn = new MySqlConnection(settings.BuildConnectionString(plainPassword));
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_conn.State != System.Data.ConnectionState.Open)
            await _conn.OpenAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ContentTableEngineInfo>> GetEnginesAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var list = new List<ContentTableEngineInfo>();
        var names = string.Join(",", ContentPublishTables.All.Select(t => $"'{t.Replace("'", "''")}'"));
        await using var cmd = new MySqlCommand(
            $"SELECT TABLE_NAME, ENGINE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=@s AND TABLE_NAME IN ({names})",
            _conn, _tx);
        cmd.Parameters.AddWithValue("@s", _schema);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new ContentTableEngineInfo
            {
                Table = rd.GetString(0),
                Engine = rd.IsDBNull(1) ? "" : rd.GetString(1),
            });
        }
        return list;
    }

    public async Task<bool> CanLockTablesAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        try
        {
            // Probe with a no-op lock on information_schema is not allowed; try LOCK + immediate UNLOCK on one table.
            // Safer: check privilege via INFORMATION_SCHEMA.USER_PRIVILEGES / schema privileges.
            await using var cmd = new MySqlCommand(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.USER_PRIVILEGES
                  WHERE GRANTEE = CONCAT('''', REPLACE(CURRENT_USER(), '@', '''@'''), '''')
                    AND PRIVILEGE_TYPE IN ('LOCK TABLES', 'ALL PRIVILEGES')",
                _conn, _tx);
            // Fallback: attempt short lock
            try
            {
                await using var lockCmd = new MySqlCommand(
                    $"LOCK TABLES `{_schema}`.`{NpcsModeloColumns.DefaultTable}` READ", _conn);
                await lockCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                await using var unlock = new MySqlCommand("UNLOCK TABLES", _conn);
                await unlock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (MySqlException)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task<ContentPublishMaxSnapshot> ReadMaxIdsAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        static async Task<int> MaxAsync(MySqlConnection conn, MySqlTransaction? tx, string schema, string table, string col, CancellationToken c)
        {
            await using var cmd = new MySqlCommand(
                $"SELECT MAX(`{col}`) FROM `{schema}`.`{table}`", conn, tx);
            var r = await cmd.ExecuteScalarAsync(c).ConfigureAwait(false);
            if (r is null or DBNull) return 0;
            return Convert.ToInt32(r, CultureInfo.InvariantCulture);
        }

        return new ContentPublishMaxSnapshot
        {
            NpcsModelo = await MaxAsync(_conn, _tx, _schema, NpcsModeloColumns.DefaultTable, NpcsModeloColumns.Id, ct).ConfigureAwait(false),
            NpcPreguntas = await MaxAsync(_conn, _tx, _schema, NpcPreguntasColumns.DefaultTable, NpcPreguntasColumns.Id, ct).ConfigureAwait(false),
            NpcRespuestas = await MaxAsync(_conn, _tx, _schema, NpcRespuestasColumns.DefaultTable, NpcRespuestasColumns.Id, ct).ConfigureAwait(false),
            Misiones = await MaxAsync(_conn, _tx, _schema, MisionesColumns.DefaultTable, MisionesColumns.Id, ct).ConfigureAwait(false),
            MisionEtapas = await MaxAsync(_conn, _tx, _schema, MisionEtapasColumns.DefaultTable, MisionEtapasColumns.Id, ct).ConfigureAwait(false),
            MisionObjetivos = await MaxAsync(_conn, _tx, _schema, MisionObjetivosColumns.DefaultTable, MisionObjetivosColumns.Id, ct).ConfigureAwait(false),
        };
    }

    public async Task<IReadOnlyList<int>> FindExistingIdsAsync(
        string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<int>();
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"SELECT `{idColumn}` FROM `{_schema}`.`{table}` WHERE `{idColumn}` IN (");
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('@').Append(i);
        }
        sb.Append(')');
        await using var cmd = new MySqlCommand(sb.ToString(), _conn, _tx);
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue("@" + i, ids[i]);
        var found = new List<int>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            found.Add(rd.GetInt32(0));
        return found;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        _tx = await _conn.BeginTransactionAsync(ct).ConfigureAwait(false);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_tx is null) return;
        await _tx.CommitAsync(ct).ConfigureAwait(false);
        await _tx.DisposeAsync().ConfigureAwait(false);
        _tx = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_tx is null) return;
        await _tx.RollbackAsync(ct).ConfigureAwait(false);
        await _tx.DisposeAsync().ConfigureAwait(false);
        _tx = null;
    }

    public async Task LockTablesWriteAsync(IReadOnlyList<string> tables, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var parts = tables.Select(t => $"`{_schema}`.`{t}` WRITE");
        await using var cmd = new MySqlCommand("LOCK TABLES " + string.Join(", ", parts), _conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _locked = true;
    }

    public async Task UnlockTablesAsync(CancellationToken ct = default)
    {
        if (!_locked) return;
        await using var cmd = new MySqlCommand("UNLOCK TABLES", _conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _locked = false;
    }

    public async Task InsertNpcAsync(NpcModeloInsertRow row, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = @"INSERT INTO `{0}`.`npcs_modelo`
(`id`,`gfxID`,`scaleX`,`scaleY`,`sexo`,`color1`,`color2`,`color3`,`accesorios`,`foto`,`pregunta`,`ventas`,`nombre`,`objetoCompra`)
VALUES (@id,@gfx,@sx,@sy,@sexo,@c1,@c2,@c3,@acc,@foto,@preg,@ventas,@nombre,@obj)";
        await using var cmd = new MySqlCommand(string.Format(CultureInfo.InvariantCulture, sql, _schema), _conn, _tx);
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@gfx", row.GfxId);
        cmd.Parameters.AddWithValue("@sx", row.ScaleX);
        cmd.Parameters.AddWithValue("@sy", row.ScaleY);
        cmd.Parameters.AddWithValue("@sexo", row.Sexo);
        cmd.Parameters.AddWithValue("@c1", row.Color1);
        cmd.Parameters.AddWithValue("@c2", row.Color2);
        cmd.Parameters.AddWithValue("@c3", row.Color3);
        cmd.Parameters.AddWithValue("@acc", row.Accesorios);
        cmd.Parameters.AddWithValue("@foto", row.Foto);
        cmd.Parameters.AddWithValue("@preg", row.Pregunta);
        cmd.Parameters.AddWithValue("@ventas", row.Ventas);
        cmd.Parameters.AddWithValue("@nombre", row.Nombre);
        cmd.Parameters.AddWithValue("@obj", row.ObjetoCompra);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertUbicacionAsync(NpcUbicacionInsertRow row, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = @"INSERT INTO `{0}`.`npcs_ubicacion`
(`mapa`,`celda`,`npc`,`orientacion`,`nombre`,`condicion`)
VALUES (@mapa,@celda,@npc,@ori,@nombre,@cond)";
        await using var cmd = new MySqlCommand(string.Format(CultureInfo.InvariantCulture, sql, _schema), _conn, _tx);
        cmd.Parameters.AddWithValue("@mapa", row.Mapa);
        cmd.Parameters.AddWithValue("@celda", row.Celda);
        cmd.Parameters.AddWithValue("@npc", row.Npc);
        cmd.Parameters.AddWithValue("@ori", row.Orientacion);
        cmd.Parameters.AddWithValue("@nombre", row.Nombre);
        cmd.Parameters.AddWithValue("@cond", row.Condicion);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertPreguntaAsync(NpcPreguntaInsertRow row, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = @"INSERT INTO `{0}`.`npc_preguntas` (`id`,`respuestas`,`params`,`alternos`)
VALUES (@id,@resp,@params,@alt)";
        await using var cmd = new MySqlCommand(string.Format(CultureInfo.InvariantCulture, sql, _schema), _conn, _tx);
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@resp", row.Respuestas);
        cmd.Parameters.AddWithValue("@params", row.Params);
        cmd.Parameters.AddWithValue("@alt", row.Alternos);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertRespuestaActionAsync(NpcRespuestaInsertRow row, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        // orden = AUTO_INCREMENT — do not assign
        const string sql = @"INSERT INTO `{0}`.`npc_respuestas` (`id`,`accion`,`args`,`condicion`)
VALUES (@id,@accion,@args,@cond)";
        await using var cmd = new MySqlCommand(string.Format(CultureInfo.InvariantCulture, sql, _schema), _conn, _tx);
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@accion", row.Accion);
        cmd.Parameters.AddWithValue("@args", row.Args);
        cmd.Parameters.AddWithValue("@cond", row.Condicion);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertMisionAsync(MisionInsertRow row, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = @"INSERT INTO `{0}`.`misiones`
(`id`,`nombre`,`etapas`,`pregDarMision`,`pregMisCompletada`,`pregMisIncompleta`,`puedeRepetirse`)
VALUES (@id,@nombre,@etapas,@dar,@comp,@inc,@rep)";
        await using var cmd = new MySqlCommand(string.Format(CultureInfo.InvariantCulture, sql, _schema), _conn, _tx);
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@nombre", row.Nombre);
        cmd.Parameters.AddWithValue("@etapas", row.Etapas);
        cmd.Parameters.AddWithValue("@dar", row.PregDarMision);
        cmd.Parameters.AddWithValue("@comp", row.PregMisCompletada);
        cmd.Parameters.AddWithValue("@inc", row.PregMisIncompleta);
        cmd.Parameters.AddWithValue("@rep", row.PuedeRepetirse);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertEtapaAsync(MisionEtapaInsertRow row, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = @"INSERT INTO `{0}`.`mision_etapas`
(`id`,`nombre`,`descripcion`,`recompensas`,`objetivos`,`variosobj`)
VALUES (@id,@nombre,@desc,@rew,@obj,@varios)";
        await using var cmd = new MySqlCommand(string.Format(CultureInfo.InvariantCulture, sql, _schema), _conn, _tx);
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@nombre", row.Nombre);
        cmd.Parameters.AddWithValue("@desc", row.Descripcion);
        cmd.Parameters.AddWithValue("@rew", row.Recompensas);
        cmd.Parameters.AddWithValue("@obj", row.Objetivos);
        cmd.Parameters.AddWithValue("@varios", row.VariosObj);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertObjetivoAsync(MisionObjetivoInsertRow row, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = @"INSERT INTO `{0}`.`mision_objetivos`
(`id`,`tipo`,`args`,`detalle`,`esalHablar`,`esOculto`,`condicion`)
VALUES (@id,@tipo,@args,@det,@hab,@oc,@cond)";
        await using var cmd = new MySqlCommand(string.Format(CultureInfo.InvariantCulture, sql, _schema), _conn, _tx);
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@tipo", row.Tipo);
        cmd.Parameters.AddWithValue("@args", row.Args);
        cmd.Parameters.AddWithValue("@det", row.Detalle);
        cmd.Parameters.AddWithValue("@hab", row.EsAlHablar);
        cmd.Parameters.AddWithValue("@oc", row.EsOculto);
        cmd.Parameters.AddWithValue("@cond", row.Condicion);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountByIdsAsync(string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM `{_schema}`.`{table}` WHERE `{idColumn}` IN (");
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('@').Append(i);
        }
        sb.Append(')');
        await using var cmd = new MySqlCommand(sb.ToString(), _conn, _tx);
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue("@" + i, ids[i]);
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(r, CultureInfo.InvariantCulture);
    }

    public async Task<int> CountUbicacionesByNpcIdsAsync(IReadOnlyList<int> npcIds, CancellationToken ct = default)
    {
        if (npcIds.Count == 0) return 0;
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM `{_schema}`.`npcs_ubicacion` WHERE `npc` IN (");
        for (var i = 0; i < npcIds.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('@').Append(i);
        }
        sb.Append(')');
        await using var cmd = new MySqlCommand(sb.ToString(), _conn, _tx);
        for (var i = 0; i < npcIds.Count; i++)
            cmd.Parameters.AddWithValue("@" + i, npcIds[i]);
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(r, CultureInfo.InvariantCulture);
    }

    public async Task<int> CountRespuestaRowsByLogicalIdsAsync(IReadOnlyList<int> responseIds, CancellationToken ct = default)
    {
        if (responseIds.Count == 0) return 0;
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM `{_schema}`.`npc_respuestas` WHERE `id` IN (");
        for (var i = 0; i < responseIds.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('@').Append(i);
        }
        sb.Append(')');
        await using var cmd = new MySqlCommand(sb.ToString(), _conn, _tx);
        for (var i = 0; i < responseIds.Count; i++)
            cmd.Parameters.AddWithValue("@" + i, responseIds[i]);
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(r, CultureInfo.InvariantCulture);
    }

    public async Task DeleteByIdsAsync(string table, string idColumn, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"DELETE FROM `{_schema}`.`{table}` WHERE `{idColumn}` IN (");
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('@').Append(i);
        }
        sb.Append(')');
        await using var cmd = new MySqlCommand(sb.ToString(), _conn, _tx);
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue("@" + i, ids[i]);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteUbicacionesByNpcIdsAsync(IReadOnlyList<int> npcIds, CancellationToken ct = default)
    {
        if (npcIds.Count == 0) return;
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"DELETE FROM `{_schema}`.`npcs_ubicacion` WHERE `npc` IN (");
        for (var i = 0; i < npcIds.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('@').Append(i);
        }
        sb.Append(')');
        await using var cmd = new MySqlCommand(sb.ToString(), _conn, _tx);
        for (var i = 0; i < npcIds.Count; i++)
            cmd.Parameters.AddWithValue("@" + i, npcIds[i]);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_locked)
        {
            try { await UnlockTablesAsync().ConfigureAwait(false); } catch { /* ignore */ }
        }
        if (_tx is not null)
        {
            try { await RollbackTransactionAsync().ConfigureAwait(false); } catch { /* ignore */ }
        }
        await _conn.DisposeAsync().ConfigureAwait(false);
    }
}
