using System.Collections.Concurrent;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.4 — in-memory mobs_fix for unit tests (simulates PK + defaults).</summary>
public sealed class InMemoryMobsFixRepository : IMobsFixRepository
{
    private readonly ConcurrentDictionary<(int Mapa, int Celda), MobsFixRow> _rows = new();
    private readonly HashSet<int> _mobModeloIds;
    private bool _schemaOk = true;

    public InMemoryMobsFixRepository(IEnumerable<int>? mobModeloIds = null)
    {
        _mobModeloIds = mobModeloIds is null
            ? new HashSet<int> { 1056, 1106, 1107, 1, 2, 3, 4, 5, 6, 7, 8 }
            : new HashSet<int>(mobModeloIds);
    }

    public int ReplaceCount { get; private set; }
    public int MapasMobsWriteCount { get; private set; }

    public void SetSchemaBroken(bool broken) => _schemaOk = !broken;

    public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task ValidateSchemaAsync(CancellationToken ct = default)
    {
        if (!_schemaOk)
            throw new MobsFixSchemaException("Simulated schema failure.");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MobsFixRow>> GetByMapaAsync(int mapa, CancellationToken ct = default)
    {
        var list = _rows.Values
            .Where(r => r.Mapa == mapa)
            .OrderBy(r => r.Celda)
            .Select(AnnotateLegacy)
            .ToList();
        return Task.FromResult<IReadOnlyList<MobsFixRow>>(list);
    }

    public Task<MobsFixRow?> GetByMapaCeldaAsync(int mapa, int celda, CancellationToken ct = default)
    {
        if (!_rows.TryGetValue((mapa, celda), out var row))
            return Task.FromResult<MobsFixRow?>(null);
        return Task.FromResult<MobsFixRow?>(AnnotateLegacy(row));
    }

    public Task ReplaceAsync(MobsFixPublishRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReplaceCount++;
        // Simulate MySQL defaults when columns are omitted from REPLACE.
        var row = new MobsFixRow
        {
            Mapa = request.Mapa,
            Celda = request.Celda,
            Mobs = request.Mobs,
            Tipo = request.Tipo,
            Condicion = request.Condicion ?? "",
            SegundosRespawn = request.SegundosRespawn,
            Descripcion = request.Descripcion ?? "",
            Sala = MobsFixColumns.ExpectedSalaDefault,
            Movible = MobsFixColumns.ExpectedMovibleDefault,
            Oleadas = MobsFixColumns.ExpectedOleadasDefault,
            Id = null,
        };
        _rows[(request.Mapa, request.Celda)] = row;
        return Task.CompletedTask;
    }

    public Task<bool> MobModeloExistsAsync(int mobId, CancellationToken ct = default) =>
        Task.FromResult(_mobModeloIds.Contains(mobId));

    /// <summary>Seed a legacy/corrupt row without going through REPLACE.</summary>
    public void SeedRaw(MobsFixRow row)
    {
        _rows[(row.Mapa, row.Celda)] = row;
    }

    private static MobsFixRow AnnotateLegacy(MobsFixRow row) =>
        new()
        {
            Mapa = row.Mapa,
            Celda = row.Celda,
            Mobs = row.Mobs,
            Tipo = row.Tipo,
            Condicion = row.Condicion,
            SegundosRespawn = row.SegundosRespawn,
            Descripcion = row.Descripcion,
            Sala = row.Sala,
            Movible = row.Movible,
            Oleadas = row.Oleadas,
            Id = row.Id,
            HasLegacyOrUnrecognizedMobsFormat = !MobsFixGroupString.IsStrictFormat(row.Mobs),
        };
}
