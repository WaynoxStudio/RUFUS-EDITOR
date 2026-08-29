namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

public interface IMobsFixRepository
{
    Task PingAsync(CancellationToken ct = default);

    /// <summary>Validates real schema: required columns + PK (mapa, celda).</summary>
    Task ValidateSchemaAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MobsFixRow>> GetByMapaAsync(int mapa, CancellationToken ct = default);

    Task<MobsFixRow?> GetByMapaCeldaAsync(int mapa, int celda, CancellationToken ct = default);

    /// <summary>
    /// REPLACE INTO only the 7 server columns. Does not write Sala/movible/oleadas/id.
    /// </summary>
    Task ReplaceAsync(MobsFixPublishRequest request, CancellationToken ct = default);

    /// <summary>True if mobs_modelo contains the id.</summary>
    Task<bool> MobModeloExistsAsync(int mobId, CancellationToken ct = default);
}

public sealed class MobsFixSchemaException : Exception
{
    public MobsFixSchemaException(string message) : base(message) { }
}

public sealed class MobsFixVerifyException : Exception
{
    public MobsFixVerifyException(string message) : base(message) { }
}
