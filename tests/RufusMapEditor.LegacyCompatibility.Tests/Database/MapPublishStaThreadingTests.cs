using System.Collections.Concurrent;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Tests.Database;

/// <summary>
/// HOTFIX 10B.1 — after DB awaits with ConfigureAwait(false), UI callbacks must run on the
/// SynchronizationContext captured at workflow start. No production DB.
/// </summary>
public sealed class MapPublishStaThreadingTests
{
    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            var previous = Current;
            try
            {
                SetSynchronizationContext(this);
                d(state);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            var previous = Current;
            try
            {
                SetSynchronizationContext(this);
                d(state);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }

    private static MapDocument Map(int id = 51001)
    {
        var cells = Enumerable.Range(0, MapGeometry.CellCount(15, 17))
            .Select(_ => new CellData())
            .ToList();
        cells[4].FightCell = 1;
        cells[8].FightCell = 2;
        var map = new MapDocument
        {
            Id = id,
            Width = 15,
            Height = 17,
            DateMap = "AME",
            BackgroundId = 12,
            BackgroundDefined = true,
            MusicId = 3,
            MusicDefined = true,
            AmbianceId = 4,
            AmbianceDefined = true,
            Outdoor = true,
            Capabilities = 7,
            CapabilitiesDefined = true,
            WorldX = -47,
            WorldY = -12,
            WorldCoordinatesSet = true,
            Cells = cells,
        };
        MapCellEditor.SyncDocument(map);
        return map;
    }

    private static MapasRow RowFrom(MapDocument map, string fecha) => new()
    {
        Id = map.Id,
        Fecha = fecha,
        Ancho = map.Width,
        Alto = map.Height,
        BgId = map.BackgroundId,
        MusicId = map.MusicId,
            AmbienteId = map.AmbianceId,
        OutDoor = map.Outdoor == true ? 1 : 0,
        Capabilities = map.Capabilities,
            PosPelea = map.FightPlaces ?? "",
        MapData = map.MapData ?? "",
        X = map.WorldX,
        Y = map.WorldY,
        Key = "k",
        Mobs = "",
        SubArea = 0,
        MaxGrupoMobs = 4,
        MaxMobsPorGrupo = 8,
        MinNivelGrupoMob = 0,
        MaxNivelGrupoMob = 0,
        MaxMercantes = 5,
        MaxPeleas = 99,
        MinMobsPorGrupo = 1,
    };

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "rufus-sta-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task Confirm_publish_callback_runs_on_captured_sync_context_after_background_hop()
    {
        var ui = new TrackingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var map = Map();
            map.BackgroundId = 99;
            MapCellEditor.SyncDocument(map);

            var repo = new InMemoryMapasRepository();
            repo.Seed(RowFrom(Map(map.Id), "1"));

            SynchronizationContext? seenAtConfirm = null;
            var leftUiDuringSave = false;

            var outcome = await MapPublishWorkflow.ExecuteAsync(
                map,
                new MapPublishService(repo, TempDir()),
                async _ =>
                {
                    await Task.Delay(20).ConfigureAwait(false);
                    leftUiDuringSave = SynchronizationContext.Current is not TrackingSynchronizationContext;
                    return true;
                },
                (_, _, _) =>
                {
                    seenAtConfirm = SynchronizationContext.Current;
                    return false; // cancel — no UPDATE
                },
                _ => null);

            Assert.True(leftUiDuringSave);
            Assert.Same(ui, seenAtConfirm);
            Assert.False(outcome.Success);
            Assert.Equal(0, repo.UpdateCount);
            Assert.Equal(0, repo.InsertCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task Create_confirm_callback_runs_on_captured_sync_context_and_cancel_skips_insert()
    {
        var ui = new TrackingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var repo = new InMemoryMapasRepository();
            repo.SetSchema(InMemoryMapasRepository.SchemaWithDbDefaultsForPreserved());

            SynchronizationContext? seen = null;
            var outcome = await MapPublishWorkflow.ExecuteAsync(
                Map(51002),
                new MapPublishService(repo, TempDir()),
                async _ =>
                {
                    await Task.Delay(15).ConfigureAwait(false);
                    return true;
                },
                (_, _, _) => true,
                _ => null,
                (_, _) =>
                {
                    seen = SynchronizationContext.Current;
                    return false;
                });

            Assert.Same(ui, seen);
            Assert.False(outcome.Success);
            Assert.Equal(0, repo.InsertCount);
            Assert.Equal(0, repo.UpdateCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task Db_work_without_sync_context_still_allows_cancel_without_write()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var map = Map(51003);
        map.BackgroundId = 5;
        MapCellEditor.SyncDocument(map);
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFrom(Map(51003), "2"));

        var outcome = await MapPublishWorkflow.ExecuteAsync(
            map,
            new MapPublishService(repo, TempDir()),
            async _ =>
            {
                await Task.Delay(5).ConfigureAwait(false);
                return true;
            },
            (_, _, _) => false,
            _ => null);

        Assert.False(outcome.Success);
        Assert.Equal(0, repo.UpdateCount);
    }
}
