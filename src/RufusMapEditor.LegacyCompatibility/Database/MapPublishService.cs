using System.Text.Json;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Database;

public sealed class PublishOutcome
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public bool NoChanges { get; init; }
    public bool MissingRow { get; init; }
    public bool NeedsManualRevision { get; init; }
    public string? CurrentFecha { get; init; }
    public string? NewFecha { get; init; }
    public string? BackupPath { get; init; }
    public PublishDiff? Diff { get; init; }
    public bool Created { get; init; }
    public MapInsertPlan? InsertPlan { get; init; }
}

public sealed class MapPublishService
{
    private readonly IMapasRepository _repo;
    private readonly string _backupDir;

    public MapPublishService(IMapasRepository repo, string backupDirectory, string databaseLabel = "estaticos.mapas")
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _backupDir = backupDirectory ?? throw new ArgumentNullException(nameof(backupDirectory));
        DatabaseLabel = databaseLabel;
    }

    public string DatabaseLabel { get; }

    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        RufusLog.Info($"Conexión BD iniciada · {DatabaseLabel}");
        try
        {
            await _repo.TestConnectionAsync(ct).ConfigureAwait(false);
            RufusLog.Ok("Conexión BD correcta");
        }
        catch (Exception ex)
        {
            RufusLog.Error("Conexión BD fallida: " + ex.Message);
            throw;
        }
    }

    public async Task<SchemaCheckResult> ValidateSchemaAsync(CancellationToken ct = default)
    {
        var cols = await _repo.ListColumnsAsync(ct).ConfigureAwait(false);
        return MapPublishLogic.CheckSchema(cols);
    }

    public Task<MapasRow?> ReadAsync(int id, CancellationToken ct = default) => _repo.TryGetAsync(id, ct);

    public async Task<MapInsertPlan> PrepareCreateAsync(
        MapDocument map,
        NewMapDefaultsSettings? defaults = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        MapCreateLogic.EnsureNewMapWorldCoordinates(map);
        var schema = await _repo.GetTableSchemaAsync(ct).ConfigureAwait(false);
        var required = MapPublishLogic.CheckSchema(schema.Columns.Select(c => c.ColumnName));
        if (!required.Ok)
        {
            return new MapInsertPlan
            {
                EditorValues = MapPublishLogic.FromDocument(map, MapCreateLogic.InitialRevision),
                Columns = Array.Empty<InsertColumnPlan>(),
                MissingRequiredDefaults = new[] { required.Message ?? "Schema inválido." },
            };
        }

        return MapCreateLogic.BuildInsertPlan(map, schema, defaults);
    }

    public async Task<PublishOutcome> PublishCreateAsync(
        MapDocument map,
        MapInsertPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.EditorValues.Id != map.Id)
            return Fail("El plan INSERT no corresponde al mapa actual.");
        if (!plan.CanInsert)
            return Fail("INSERT bloqueado:\n" + string.Join("\n", plan.MissingRequiredDefaults));

        RufusLog.Info($"Publicación CREATE iniciada · mapa {map.Id}");
        var backup = WriteInsertIntent(plan);
        try
        {
            var affected = await _repo.InsertNewAsync(plan, ct).ConfigureAwait(false);
            if (affected != 1)
            {
                WritePublishLog(map.Id, "CREATE", $"ERROR affected_rows={affected}");
                RufusLog.Error($"CREATE fallido · affected_rows={affected}");
                return Fail($"INSERT no confirmado: affected_rows={affected}; se esperaba exactamente 1.");
            }

            var verify = await _repo.TryGetAsync(map.Id, ct).ConfigureAwait(false);
            if (verify is null || !EditorValuesEqual(verify, plan.EditorValues))
            {
                WritePublishLog(map.Id, "CREATE", "ERROR verify");
                RufusLog.Error("CREATE: verificación final fallida");
                return Fail("Verification failed: los campos del editor no coinciden tras INSERT.");
            }

            map.DateMap = MapCreateLogic.InitialRevision;
            WritePublishLog(map.Id, "CREATE", "OK");
            RufusLog.Ok($"Publicación CREATE completada · mapa {map.Id} · revisión {MapCreateLogic.InitialRevision}");
            RufusLog.Info("Verificación final CREATE correcta");
            return new PublishOutcome
            {
                Success = true,
                Created = true,
                CurrentFecha = null,
                NewFecha = MapCreateLogic.InitialRevision,
                BackupPath = backup,
                InsertPlan = plan,
            };
        }
        catch (Exception ex)
        {
            WritePublishLog(map.Id, "CREATE", "ERROR " + ex.Message);
            RufusLog.Error("CREATE error: " + ex.Message);
            return Fail(ex.Message);
        }
    }

    public async Task<PublishOutcome> PrepareAsync(MapDocument map, string? manualRevision = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Id <= 0)
            return Fail("MapId inválido.");
        var schema = await ValidateSchemaAsync(ct).ConfigureAwait(false);
        if (!schema.Ok)
            return Fail(schema.Message ?? "Schema inválido.");

        var row = await _repo.TryGetAsync(map.Id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return new PublishOutcome
            {
                Success = false,
                MissingRow = true,
                Error =
                    $"El mapa {map.Id} no existe en la base de datos.\n" +
                    "La creación de mapas nuevos se implementará posteriormente.",
            };
        }

        if (MapPublishLogic.ContentMatchesDb(row, map))
        {
            return new PublishOutcome
            {
                Success = true,
                NoChanges = true,
                CurrentFecha = row.Fecha,
                NewFecha = row.Fecha,
                Diff = MapPublishLogic.BuildDiff(row, map, row.Fecha),
                Error = "No hay cambios que publicar.",
            };
        }

        string proposed;
        if (RevisionLogic.TryIncrement(row.Fecha, out var auto, out _))
        {
            proposed = auto;
        }
        else if (!string.IsNullOrWhiteSpace(manualRevision) && RevisionLogic.IsNumeric(manualRevision))
        {
            proposed = manualRevision.Trim();
        }
        else
        {
            return new PublishOutcome
            {
                Success = false,
                NeedsManualRevision = true,
                CurrentFecha = row.Fecha,
                Diff = MapPublishLogic.BuildDiff(row, map, row.Fecha),
                Error =
                    $"El valor de revisión actual no es numérico: {row.Fecha}\n" +
                    "Indique una revisión nueva válida (entero) antes de publicar.",
            };
        }

        return new PublishOutcome
        {
            Success = true,
            CurrentFecha = row.Fecha,
            NewFecha = proposed,
            Diff = MapPublishLogic.BuildDiff(row, map, proposed),
        };
    }

    public async Task<PublishOutcome> PublishAsync(MapDocument map, string newFecha, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Id <= 0)
            return Fail("MapId inválido.");
        if (string.IsNullOrWhiteSpace(newFecha))
            return Fail("Revisión nueva vacía.");

        RufusLog.Info($"Publicación UPDATE iniciada · mapa {map.Id}");
        var schema = await ValidateSchemaAsync(ct).ConfigureAwait(false);
        if (!schema.Ok)
            return Fail(schema.Message ?? "Schema inválido.");

        var row = await _repo.TryGetAsync(map.Id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return new PublishOutcome
            {
                Success = false,
                MissingRow = true,
                Error =
                    $"El mapa {map.Id} no existe en la base de datos.\n" +
                    "La creación de mapas nuevos se implementará posteriormente.",
            };
        }

        if (MapPublishLogic.ContentMatchesDb(row, map))
        {
            return new PublishOutcome
            {
                Success = true,
                NoChanges = true,
                CurrentFecha = row.Fecha,
                NewFecha = row.Fecha,
                Error = "No hay cambios que publicar.",
            };
        }

        RufusLog.Info($"Revisión {row.Fecha} → {newFecha.Trim()}");
        var values = MapPublishLogic.FromResolved(map, row, newFecha.Trim());
        var backup = WriteBackup(row);
        try
        {
            var affected = await _repo.UpdateExistingAsync(values, ct).ConfigureAwait(false);
            if (affected > 1)
            {
                RufusLog.Error($"UPDATE crítico: affected_rows={affected}");
                return Fail($"UPDATE crítico: affected_rows={affected}.");
            }

            map.DateMap = values.Fecha;
            WritePublishLog(map.Id, "UPDATE", "OK");
            RufusLog.Ok($"Publicación UPDATE completada · mapa {map.Id}");
            RufusLog.Info("Verificación final UPDATE: escritura confirmada");
            return new PublishOutcome
            {
                Success = true,
                CurrentFecha = row.Fecha,
                NewFecha = values.Fecha,
                BackupPath = backup,
                Diff = MapPublishLogic.BuildDiff(row, map, values.Fecha),
            };
        }
        catch (Exception ex)
        {
            WritePublishLog(map.Id, "UPDATE", "ERROR " + ex.Message);
            RufusLog.Error("UPDATE error: " + ex.Message);
            return Fail(ex.Message);
        }
    }

    private string WriteBackup(MapasRow row)
    {
        Directory.CreateDirectory(_backupDir);
        var path = Path.Combine(_backupDir, $"mapas_{row.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(row, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private string WriteInsertIntent(MapInsertPlan plan)
    {
        Directory.CreateDirectory(_backupDir);
        var path = Path.Combine(
            _backupDir,
            $"mapas_{plan.EditorValues.Id}_INSERT_INTENT_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json");
        var payload = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Operation = "CREATE",
            MapId = plan.EditorValues.Id,
            EditorValues = plan.EditorValues,
            Columns = plan.Columns,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private void WritePublishLog(int mapId, string operation, string result)
    {
        Directory.CreateDirectory(_backupDir);
        var line = $"{DateTimeOffset.UtcNow:O}\tMapId={mapId}\t{operation}\t{result}{Environment.NewLine}";
        File.AppendAllText(Path.Combine(_backupDir, "database-publish.log"), line);
    }

    private static bool EditorValuesEqual(MapasRow row, MapPublishValues values) =>
        row.Id == values.Id
        && string.Equals(row.Fecha, values.Fecha, StringComparison.Ordinal)
        && row.Ancho == values.Ancho
        && row.Alto == values.Alto
        && row.BgId == values.BgId
        && row.MusicId == values.MusicId
        && row.AmbienteId == values.AmbienteId
        && row.OutDoor == values.OutDoor
        && row.Capabilities == values.Capabilities
        && string.Equals(row.PosPelea, values.PosPelea, StringComparison.Ordinal)
        && string.Equals(row.MapData, values.MapData, StringComparison.Ordinal)
        && row.X == values.X
        && row.Y == values.Y;

    
    public async Task<(bool Ok, string? Error, DatabaseMapSnapshot? Snapshot)> SyncMetadataFromDatabaseAsync(
        MapDocument map,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Id <= 0)
            return (false, "MapId inválido.", null);

        var schema = await ValidateSchemaAsync(ct).ConfigureAwait(false);
        if (!schema.Ok)
            return (false, schema.Message ?? "Schema inválido.", null);

        var row = await _repo.TryGetAsync(map.Id, ct).ConfigureAwait(false);
        if (row is null)
            return (false, $"El mapa {map.Id} no existe en la base de datos.", null);

        var beforeMapData = map.MapData;
        var beforeFight = map.FightPlaces;
        var snap = MapPublishLogic.SyncMetadataFromDatabase(map, row);
        if (!string.Equals(beforeMapData, map.MapData, StringComparison.Ordinal)
            || !string.Equals(beforeFight, map.FightPlaces, StringComparison.Ordinal))
            return (false, "INTERNAL: SyncMetadata no debe alterar MapData ni FightPlaces.", null);

        return (true, null, snap);
    }

    private static PublishOutcome Fail(string msg) => new() { Success = false, Error = msg };
}
