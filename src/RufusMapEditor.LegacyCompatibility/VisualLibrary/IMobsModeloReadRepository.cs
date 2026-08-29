namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

public static class MobsModeloColumns
{
    public const string DefaultDatabase = "estaticos";
    public const string DefaultTable = "mobs_modelo";
    public const string Id = "id";
    public const string Nombre = "nombre";
    public const string GfxId = "gfxID";
    public const string Grados = "grados";
}

public interface IMobsModeloReadRepository
{
    /// <summary>READ-ONLY: id, nombre, gfxID, grados.</summary>
    Task<IReadOnlyList<MobsModeloRow>> GetAllAsync(CancellationToken ct = default);
}

public sealed class MobsModeloRow
{
    public required int Id { get; init; }
    public required string Nombre { get; init; }
    public required int GfxId { get; init; }
    public required string Grados { get; init; }
}
