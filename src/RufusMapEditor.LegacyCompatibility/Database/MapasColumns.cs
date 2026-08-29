namespace RufusMapEditor.LegacyCompatibility.Database;

/// <summary>Confirmed MySQL identifiers for estaticos.mapas (FASE 10A). Do not invent columns.</summary>
public static class MapasColumns
{
    public const string DefaultDatabase = "estaticos";
    public const string DefaultTable = "mapas";

    public const string Id = "id";
    public const string Fecha = "fecha";
    public const string Ancho = "ancho";
    public const string Alto = "alto";
    public const string BgId = "bgID";
    public const string MusicId = "musicID";
    public const string AmbienteId = "ambienteID";
    public const string OutDoor = "outDoor";
    public const string Capabilities = "capabilities";
    public const string PosPelea = "posPelea";
    public const string MapData = "mapData";
    public const string X = "X";
    public const string Y = "Y";
    public const string Key = "key";
    public const string Mobs = "mobs";
    public const string SubArea = "subArea";
    public const string MaxGrupoMobs = "maxGrupoMobs";
    public const string MaxMobsPorGrupo = "maxMobsPorGrupo";
    public const string MinNivelGrupoMob = "minNivelGrupoMob";
    public const string MaxNivelGrupoMob = "maxNivelGrupoMob";
    public const string MaxMercantes = "maxMercantes";
    public const string MaxPeleas = "maxPeleas";
    public const string MinMobsPorGrupo = "minMobsPorGrupo";

    public static readonly string[] Required =
    [
        Id, Fecha, Ancho, Alto, BgId, MusicId, AmbienteId, OutDoor,
        Capabilities, PosPelea, MapData, X, Y,
    ];

    public static readonly string[] Preserved =
    [
        Key, Mobs, SubArea, MaxGrupoMobs, MaxMobsPorGrupo,
        MinNivelGrupoMob, MaxNivelGrupoMob, MaxMercantes, MaxPeleas, MinMobsPorGrupo,
    ];

    public static readonly string[] Updated =
    [
        Fecha, Ancho, Alto, BgId, MusicId, AmbienteId, OutDoor,
        Capabilities, PosPelea, MapData, X, Y,
    ];
}
