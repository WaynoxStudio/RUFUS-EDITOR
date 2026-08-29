namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.4 — one persistent fixed-mob group row (mapa+celda PK).</summary>
public sealed class MobsFixRow
{
    public int Mapa { get; init; }
    public int Celda { get; init; }
    public string Mobs { get; init; } = "";
    public int Tipo { get; init; }
    public string Condicion { get; init; } = "";
    public int SegundosRespawn { get; init; }
    public string Descripcion { get; init; } = "";

    /// <summary>DB default when omitted from REPLACE.</summary>
    public string? Sala { get; init; }

    public int? Movible { get; init; }
    public int? Oleadas { get; init; }
    public long? Id { get; init; }

    public bool HasLegacyOrUnrecognizedMobsFormat { get; init; }
}
