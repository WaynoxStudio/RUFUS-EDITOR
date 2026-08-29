namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// Visual catalog for npcs_ubicacion.orientacion (8 isometric facings).
/// Raw int is still stored; 0 remains valid/unset for BD compatibility.
/// </summary>
public static class NpcOrientationCatalog
{
    public const int Unset = 0;
    public const int MinVisual = 1;
    public const int MaxVisual = 8;

    public static bool IsVisualDirection(int orientation) =>
        orientation is >= MinVisual and <= MaxVisual;

    public static string GetFriendlyName(int orientation) => orientation switch
    {
        1 => "Abajo-derecha",
        2 => "Abajo",
        3 => "Abajo-izquierda",
        4 => "Izquierda",
        5 => "Arriba-izquierda",
        6 => "Arriba",
        7 => "Arriba-derecha",
        8 => "Derecha",
        0 => "Sin definir",
        _ => "Personalizada",
    };

    public static string FormatSelectedLabel(int orientation)
    {
        if (IsVisualDirection(orientation))
            return $"Orientación seleccionada: {orientation} ({GetFriendlyName(orientation)})";
        if (orientation == Unset)
            return "Orientación seleccionada: — (sin definir)";
        return $"Orientación seleccionada: {orientation} ({GetFriendlyName(orientation)})";
    }
}
