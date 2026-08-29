namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.4 — validated payload ready for REPLACE INTO mobs_fix.</summary>
public sealed class MobsFixPublishRequest
{
    public int Mapa { get; init; }
    public int Celda { get; init; }
    public string Mobs { get; init; } = "";
    public int Tipo { get; init; }
    public string Condicion { get; init; } = "";
    public int SegundosRespawn { get; init; }
    public string Descripcion { get; init; } = "";
    public IReadOnlyList<MobsFixSlot> Slots { get; init; } = Array.Empty<MobsFixSlot>();
    public bool ReplacingExisting { get; init; }
    public MobsFixRow? ExistingRow { get; init; }
}
