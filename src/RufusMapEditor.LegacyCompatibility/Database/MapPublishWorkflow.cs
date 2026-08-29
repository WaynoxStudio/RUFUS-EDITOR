using System.Runtime.ExceptionServices;
using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Database;

/// <summary>
/// Official Save → prepare → confirm → publish. UI supplies delegates; tests mock them.
/// DB work may resume on a thread-pool thread; UI delegates are always marshalled back to the
/// SynchronizationContext captured when <see cref="ExecuteAsync"/> started (WPF UI STA).
/// </summary>
public static class MapPublishWorkflow
{
    public static async Task<PublishOutcome> ExecuteAsync(
        MapDocument map,
        MapPublishService service,
        Func<CancellationToken, Task<bool>> officialSaveAsync,
        Func<PublishDiff, string, string, bool> confirmPublish,
        Func<string, string?> askManualRevision,
        CancellationToken ct = default)
        => await ExecuteCoreAsync(
            map,
            service,
            officialSaveAsync,
            confirmPublish,
            askManualRevision,
            confirmCreate: null,
            createDefaults: null,
            ct).ConfigureAwait(false);

    public static async Task<PublishOutcome> ExecuteAsync(
        MapDocument map,
        MapPublishService service,
        Func<CancellationToken, Task<bool>> officialSaveAsync,
        Func<PublishDiff, string, string, bool> confirmPublish,
        Func<string, string?> askManualRevision,
        Func<MapInsertPlan, string, bool> confirmCreate,
        NewMapDefaultsSettings? createDefaults = null,
        CancellationToken ct = default)
        => await ExecuteCoreAsync(
            map,
            service,
            officialSaveAsync,
            confirmPublish,
            askManualRevision,
            confirmCreate,
            createDefaults,
            ct).ConfigureAwait(false);

    private static async Task<PublishOutcome> ExecuteCoreAsync(
        MapDocument map,
        MapPublishService service,
        Func<CancellationToken, Task<bool>> officialSaveAsync,
        Func<PublishDiff, string, string, bool> confirmPublish,
        Func<string, string?> askManualRevision,
        Func<MapInsertPlan, string, bool>? confirmCreate,
        NewMapDefaultsSettings? createDefaults,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(officialSaveAsync);
        ArgumentNullException.ThrowIfNull(confirmPublish);
        ArgumentNullException.ThrowIfNull(askManualRevision);

        // Capture UI sync context once (DispatcherSynchronizationContext on WPF). Null in unit tests.
        var ui = SynchronizationContext.Current;

        if (!await RunOnContextAsync(ui, () => officialSaveAsync(ct)).ConfigureAwait(false))
        {
            return new PublishOutcome
            {
                Success = false,
                Error = "Official Save falló; no se publicó en BD.",
            };
        }

        var prepare = await service.PrepareAsync(map, manualRevision: null, ct).ConfigureAwait(false);
        if (prepare.NoChanges)
            return prepare;
        if (prepare.MissingRow && confirmCreate is not null)
        {
            var plan = await service.PrepareCreateAsync(map, createDefaults, ct).ConfigureAwait(false);
            if (!plan.CanInsert)
            {
                return new PublishOutcome
                {
                    Success = false,
                    MissingRow = true,
                    InsertPlan = plan,
                    Error = "No se puede crear el mapa:\n" + string.Join("\n", plan.MissingRequiredDefaults),
                };
            }

            var summary = MapCreateLogic.FormatCreateSummary(plan, map);
            if (!RunOnContext(ui, () => confirmCreate(plan, summary)))
                return new PublishOutcome { Success = false, MissingRow = true, Error = "Creación cancelada." };

            var created = await service.PublishCreateAsync(map, plan, ct).ConfigureAwait(false);
            return await CompleteWithOfficialSaveAsync(created, officialSaveAsync, ui, ct).ConfigureAwait(false);
        }
        if (!prepare.Success)
        {
            if (!prepare.NeedsManualRevision)
                return prepare;

            var manual = RunOnContext(ui, () => askManualRevision(prepare.CurrentFecha ?? ""));
            if (string.IsNullOrWhiteSpace(manual) || !RevisionLogic.IsNumeric(manual))
            {
                return new PublishOutcome
                {
                    Success = false,
                    NeedsManualRevision = true,
                    CurrentFecha = prepare.CurrentFecha,
                    Error = prepare.Error,
                };
            }

            prepare = await service.PrepareAsync(map, manual, ct).ConfigureAwait(false);
            if (!prepare.Success || prepare.NoChanges)
                return prepare;
        }

        if (prepare.Diff is null || string.IsNullOrWhiteSpace(prepare.NewFecha))
            return new PublishOutcome { Success = false, Error = "Diff de publicación incompleto." };

        if (!RunOnContext(ui, () => confirmPublish(prepare.Diff, prepare.CurrentFecha ?? "", prepare.NewFecha)))
        {
            return new PublishOutcome
            {
                Success = false,
                Error = "Publicación cancelada.",
            };
        }

        var published = await service.PublishAsync(map, prepare.NewFecha, ct).ConfigureAwait(false);
        if (!published.Success || published.NoChanges)
            return published;

        return await CompleteWithOfficialSaveAsync(published, officialSaveAsync, ui, ct).ConfigureAwait(false);
    }

    private static async Task<PublishOutcome> CompleteWithOfficialSaveAsync(
        PublishOutcome published,
        Func<CancellationToken, Task<bool>> officialSaveAsync,
        SynchronizationContext? ui,
        CancellationToken ct)
    {
        if (!published.Success || published.NoChanges)
            return published;
        if (await RunOnContextAsync(ui, () => officialSaveAsync(ct)).ConfigureAwait(false))
            return published;

        return new PublishOutcome
        {
            Success = false,
            Created = published.Created,
            CurrentFecha = published.CurrentFecha,
            NewFecha = published.NewFecha,
            BackupPath = published.BackupPath,
            InsertPlan = published.InsertPlan,
            Error =
                $"Mapa publicado en BD (revisión {published.NewFecha}), " +
                "pero falló el Official Save local posterior. Guarde el .rufmap manualmente.",
        };
    }

    /// <summary>Runs a synchronous UI callback on the captured context (or inline if none / already there).</summary>
    internal static T RunOnContext<T>(SynchronizationContext? context, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (context is null || ReferenceEquals(SynchronizationContext.Current, context))
            return action();

        T? result = default;
        Exception? error = null;
        context.Send(_ =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        }, null);

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }

    /// <summary>Runs an async operation on the captured context (Official Save / UI-bound work).</summary>
    internal static Task<T> RunOnContextAsync<T>(SynchronizationContext? context, Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (context is null || ReferenceEquals(SynchronizationContext.Current, context))
            return action();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ =>
        {
            _ = InvokeAsync();

            async Task InvokeAsync()
            {
                try
                {
                    var value = await action().ConfigureAwait(true);
                    tcs.TrySetResult(value);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        }, null);
        return tcs.Task;
    }
}
