using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.MapPublishQueue;

public sealed class MapBatchDbPlanItem
{
    public required int MapId { get; init; }
    public required MapDocument Document { get; init; }
    public required MapPublishQueueItem QueueItem { get; init; }
    public required bool IsInsert { get; init; }
    public bool DbNoOp { get; init; }
    public string? CurrentFecha { get; init; }
    public string? NewFecha { get; init; }
    public PublishDiff? Diff { get; init; }
    public MapInsertPlan? InsertPlan { get; init; }
    public string? Error { get; init; }
    public bool Ok => string.IsNullOrWhiteSpace(Error);
}

public sealed class MapBatchPrepareResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<MapBatchDbPlanItem> Items { get; init; } = Array.Empty<MapBatchDbPlanItem>();
    public int? MapsEsSourceVersion { get; init; }
    public int? MapsEsTargetVersion { get; init; }
}

public sealed class MapBatchMapResult
{
    public required int MapId { get; init; }
    public bool DbOk { get; set; }
    public bool ClientOk { get; set; }
    public string? Error { get; set; }
    public bool Complete => DbOk && ClientOk && string.IsNullOrWhiteSpace(Error);
}

public sealed class MapBatchPublishResult
{
    public required bool Success { get; init; }
    public bool Partial { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<MapBatchMapResult> PerMap { get; init; } = Array.Empty<MapBatchMapResult>();
    public int? MapsEsSourceVersion { get; init; }
    public int? MapsEsTargetVersion { get; init; }
    public bool VersionsUpdated { get; init; }
    public IReadOnlyList<int> CompletedMapIds { get; init; } = Array.Empty<int>();
}

/// <summary>
/// MAP-BATCH.1 — prepare BD plans with existing MapPublishService (no writes until Execute).
/// </summary>
public static class MapBatchPublishPlanner
{
    public static async Task<MapBatchPrepareResult> PrepareDatabaseAsync(
        IReadOnlyList<(MapDocument Doc, MapPublishQueueItem Queue)> maps,
        MapPublishService service,
        NewMapDefaultsSettings? createDefaults,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(service);

        if (maps.Count == 0)
            return new MapBatchPrepareResult { Success = false, Error = "Lote vacío." };

        try
        {
            await service.TestConnectionAsync(ct).ConfigureAwait(false);
            var schema = await service.ValidateSchemaAsync(ct).ConfigureAwait(false);
            if (!schema.Ok)
                return new MapBatchPrepareResult { Success = false, Error = schema.Message ?? "Schema inválido." };
        }
        catch (Exception ex)
        {
            return new MapBatchPrepareResult { Success = false, Error = "Conexión BD: " + ex.Message };
        }

        var items = new List<MapBatchDbPlanItem>();
        foreach (var (doc, queue) in maps)
        {
            if (doc.Id <= 0)
            {
                items.Add(FailItem(doc, queue, "MapId inválido."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(doc.MapData))
            {
                items.Add(FailItem(doc, queue, "MapData vacío."));
                continue;
            }

            var prepare = await service.PrepareAsync(doc, manualRevision: null, ct).ConfigureAwait(false);
            if (prepare.NoChanges && !prepare.MissingRow)
            {
                // BD already matches; LANG may still need publish.
                items.Add(new MapBatchDbPlanItem
                {
                    MapId = doc.Id,
                    Document = doc,
                    QueueItem = queue,
                    IsInsert = false,
                    DbNoOp = true,
                    CurrentFecha = prepare.CurrentFecha,
                    NewFecha = prepare.CurrentFecha,
                    Diff = prepare.Diff,
                    Error = null,
                });
                continue;
            }

            if (prepare.MissingRow)
            {
                var plan = await service.PrepareCreateAsync(doc, createDefaults, ct).ConfigureAwait(false);
                if (!plan.CanInsert)
                {
                    items.Add(FailItem(doc, queue,
                        "No se puede INSERT:\n" + string.Join("\n", plan.MissingRequiredDefaults)));
                    continue;
                }

                items.Add(new MapBatchDbPlanItem
                {
                    MapId = doc.Id,
                    Document = doc,
                    QueueItem = queue,
                    IsInsert = true,
                    InsertPlan = plan,
                    NewFecha = MapCreateLogic.InitialRevision,
                });
                continue;
            }

            if (prepare.NeedsManualRevision)
            {
                items.Add(FailItem(doc, queue,
                    prepare.Error ?? $"Revisión no numérica ({prepare.CurrentFecha}); resuélvala antes del lote."));
                continue;
            }

            if (!prepare.Success || prepare.Diff is null || string.IsNullOrWhiteSpace(prepare.NewFecha))
            {
                items.Add(FailItem(doc, queue, prepare.Error ?? "Prepare UPDATE falló."));
                continue;
            }

            items.Add(new MapBatchDbPlanItem
            {
                MapId = doc.Id,
                Document = doc,
                QueueItem = queue,
                IsInsert = false,
                CurrentFecha = prepare.CurrentFecha,
                NewFecha = prepare.NewFecha,
                Diff = prepare.Diff,
            });
        }

        var firstError = items.FirstOrDefault(i => !i.Ok);
        if (firstError is not null)
        {
            return new MapBatchPrepareResult
            {
                Success = false,
                Error = $"Mapa {firstError.MapId}: {firstError.Error}",
                Items = items,
            };
        }

        return new MapBatchPrepareResult { Success = true, Items = items };
    }

    public static async Task<MapBatchPublishResult> ExecuteDatabaseSequentialAsync(
        IReadOnlyList<MapBatchDbPlanItem> plans,
        MapPublishService service,
        CancellationToken ct = default)
    {
        var results = plans.Select(p => new MapBatchMapResult { MapId = p.MapId }).ToList();
        var completed = new List<int>();

        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var row = results[i];
            try
            {
                if (plan.IsInsert)
                {
                    if (plan.InsertPlan is null)
                    {
                        row.Error = "Plan INSERT ausente.";
                        return Partial(results, completed, row.Error);
                    }

                    var created = await service.PublishCreateAsync(plan.Document, plan.InsertPlan, ct)
                        .ConfigureAwait(false);
                    if (!created.Success)
                    {
                        row.Error = created.Error ?? "INSERT falló.";
                        return Partial(results, completed, row.Error);
                    }

                    row.DbOk = true;
                    completed.Add(plan.MapId);
                    continue;
                }

                if (plan.DbNoOp)
                {
                    row.DbOk = true;
                    completed.Add(plan.MapId);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(plan.NewFecha))
                {
                    row.Error = "NewFecha ausente para UPDATE.";
                    return Partial(results, completed, row.Error);
                }

                var published = await service.PublishAsync(plan.Document, plan.NewFecha, ct).ConfigureAwait(false);
                if (published.NoChanges)
                {
                    row.DbOk = true;
                    completed.Add(plan.MapId);
                    continue;
                }

                if (!published.Success)
                {
                    row.Error = published.Error ?? "UPDATE falló.";
                    return Partial(results, completed, row.Error);
                }

                row.DbOk = true;
                completed.Add(plan.MapId);
            }
            catch (Exception ex)
            {
                row.Error = ex.Message;
                return Partial(results, completed, ex.Message);
            }
        }

        return new MapBatchPublishResult
        {
            Success = true,
            PerMap = results,
            CompletedMapIds = completed,
        };
    }

    private static MapBatchPublishResult Partial(
        List<MapBatchMapResult> results,
        List<int> completed,
        string error) =>
        new()
        {
            Success = false,
            Partial = completed.Count > 0,
            Error = error,
            PerMap = results,
            CompletedMapIds = completed,
        };

    private static MapBatchDbPlanItem FailItem(MapDocument doc, MapPublishQueueItem queue, string error) =>
        new()
        {
            MapId = doc.Id,
            Document = doc,
            QueueItem = queue,
            IsInsert = false,
            Error = error,
        };
}
