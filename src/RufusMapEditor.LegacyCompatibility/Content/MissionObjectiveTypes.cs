namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>ADMIN.UI.4A.1 / 4B — objective tipo constants from MisionObjetivoModelo.</summary>
public static class MissionObjectiveTypes
{
    public const int Manual = 0;
    public const int TalkToNpc = 1;
    public const int ShowItemToNpc = 2;
    public const int DeliverItemsToNpc = 3;
    public const int DiscoverMap = 4;
    public const int DiscoverArea = 5;
    public const int DefeatMobs = 6;
    public const int DefeatMobAlt = 7;
    public const int UseItem = 8;
    public const int ReturnToNpc = 9;
    public const int Free10 = 10;
    public const int Free11 = 11;
    public const int DeliverSouls = 12;
    public const int Free13 = 13;
    public const int ReachLevel = 14;
    public const int HaveSpells = 15;
    public const int JobLevel = 16;

    /// <summary>Types offered in Contendido 2.0 normal UI (excludes 7/10/11/13; 12 blocked).</summary>
    public static readonly int[] UiNormalTypes =
    [
        Manual, TalkToNpc, ShowItemToNpc, DeliverItemsToNpc,
        DiscoverMap, DiscoverArea, DefeatMobs, UseItem, ReturnToNpc,
        ReachLevel, HaveSpells, JobLevel,
    ];

    public static string DisplayName(int tipo) => tipo switch
    {
        Manual => "Objetivo manual",
        TalkToNpc => "Hablar con NPC",
        ShowItemToNpc => "Enseñar objeto a NPC",
        DeliverItemsToNpc => "Entregar objeto a NPC",
        DiscoverMap => "Descubrir mapa",
        DiscoverArea => "Descubrir zona",
        DefeatMobs => "Vencer monstruos",
        DefeatMobAlt => "Vencer monstruo (alt.)",
        UseItem => "Utilizar objeto",
        ReturnToNpc => "Volver a ver NPC",
        DeliverSouls => "Entregar almas",
        ReachLevel => "Alcanzar nivel",
        HaveSpells => "Tener hechizos",
        JobLevel => "Nivel de oficio",
        Free10 => "Tipo 10 (avanzado)",
        Free11 => "Tipo 11 (avanzado)",
        Free13 => "Tipo 13 (avanzado)",
        _ => $"Tipo {tipo}",
    };

    public static bool IsUiNormal(int tipo) =>
        Array.IndexOf(UiNormalTypes, tipo) >= 0;

    public static bool SupportsCoordinates(int tipo) =>
        tipo is TalkToNpc or ShowItemToNpc or DeliverItemsToNpc
            or DefeatMobs or DefeatMobAlt or ReturnToNpc;
}
