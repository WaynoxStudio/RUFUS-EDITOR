namespace RufusMapEditor.LegacyCompatibility.Database;

/// <summary>
/// In-memory baseline of an existing <c>estaticos.mapas</c> row (HOTFIX 10A.2).
/// Does not include MapData for hydration — MapData stays editor-owned.
/// </summary>
public sealed class DatabaseMapSnapshot
{
    public required int Id { get; init; }
    public required string Fecha { get; init; }
    public required int Ancho { get; init; }
    public required int Alto { get; init; }
    public required int BgId { get; init; }
    public required int MusicId { get; init; }
    public required int AmbienteId { get; init; }
    public required int OutDoor { get; init; }
    public required int Capabilities { get; init; }
    public required string PosPelea { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }

    public static DatabaseMapSnapshot FromRow(MapasRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new DatabaseMapSnapshot
        {
            Id = row.Id,
            Fecha = row.Fecha ?? "",
            Ancho = row.Ancho,
            Alto = row.Alto,
            BgId = row.BgId,
            MusicId = row.MusicId,
            AmbienteId = row.AmbienteId,
            OutDoor = row.OutDoor,
            Capabilities = row.Capabilities,
            PosPelea = row.PosPelea ?? "",
            X = row.X,
            Y = row.Y,
        };
    }
}
