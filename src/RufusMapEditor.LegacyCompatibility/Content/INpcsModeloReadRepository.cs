namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>READ-ONLY access to npcs_modelo for provisional ID allocation. No INSERT/UPDATE/DELETE.</summary>
public interface INpcsModeloReadRepository
{
    /// <summary>SELECT MAX(id) FROM npcs_modelo. Returns 0 if the table is empty.</summary>
    Task<int> GetMaxIdAsync(CancellationToken ct = default);
}

public sealed class FixedNpcsModeloReadRepository : INpcsModeloReadRepository
{
    private readonly int _maxId;

    public FixedNpcsModeloReadRepository(int maxId) => _maxId = maxId;

    public Task<int> GetMaxIdAsync(CancellationToken ct = default) => Task.FromResult(_maxId);
}
