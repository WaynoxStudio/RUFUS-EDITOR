namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.4 — confirmed MySQL identifiers for estaticos.mobs_fix.</summary>
public static class MobsFixColumns
{
    public const string DefaultDatabase = "estaticos";
    public const string DefaultTable = "mobs_fix";

    public const string Mapa = "mapa";
    public const string Celda = "celda";
    public const string Mobs = "mobs";
    public const string Tipo = "tipo";
    public const string Condicion = "condicion";
    public const string SegundosRespawn = "segundosRespawn";
    public const string Descripcion = "descripcion";

    /// <summary>Omitted on REPLACE — DB defaults apply.</summary>
    public const string Sala = "Sala";
    public const string Movible = "movible";
    public const string Oleadas = "oleadas";
    public const string Id = "id";

    public static readonly string[] WriteColumns =
    [
        Mapa, Celda, Mobs, Tipo, Condicion, SegundosRespawn, Descripcion,
    ];

    public static readonly string[] RequiredSchemaColumns =
    [
        Mapa, Celda, Mobs, Tipo, Condicion, SegundosRespawn, Descripcion,
        Sala, Movible, Oleadas, Id,
    ];

    public const string ExpectedSalaDefault = "0";
    public const int ExpectedMovibleDefault = 1;
    public const int ExpectedOleadasDefault = 0;
}
