namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>Fallback labels when items_es type catalog is not extracted (LIB.2).</summary>
public static class DofusItemTypeNames
{
    private static readonly Dictionary<int, string> Map = new()
    {
        [1] = "Amuletos",
        [2] = "Arcos",
        [3] = "Varitas",
        [4] = "Bastones",
        [5] = "Dagas",
        [6] = "Espadas",
        [7] = "Martillos",
        [8] = "Palas",
        [9] = "Anillos",
        [10] = "Cinturones",
        [11] = "Botas",
        [12] = "Pociones",
        [13] = "Pergaminos",
        [15] = "Diversos",
        [16] = "Sombrero",
        [17] = "Capa",
        [18] = "Mascota",
        [19] = "Hacha",
        [20] = "Herramienta",
        [21] = "Pico",
        [22] = "Guadaña",
        [23] = "Dofus",
        [24] = "Misión",
        [25] = "Documento",
        [26] = "Cócteles",
        [27] = "Objetos de crianza",
        [30] = "Mimobionte",
        [31] = "Árbol",
        [42] = "Montura",
        [82] = "Escudo",
    };

    public static string Resolve(int typeId, IReadOnlyDictionary<int, string>? fromLang)
    {
        if (fromLang is not null && fromLang.TryGetValue(typeId, out var lang) && !string.IsNullOrWhiteSpace(lang))
            return lang;
        if (Map.TryGetValue(typeId, out var known))
            return known;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Tipo {typeId}");
    }
}
