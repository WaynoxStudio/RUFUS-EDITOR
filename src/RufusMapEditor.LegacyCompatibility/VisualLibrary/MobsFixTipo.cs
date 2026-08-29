namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

public enum MobsFixTipo : int
{
    Fijo = -1,
    NormalForcedFijo = 0,
    SoloUnaPelea = 1,
    HastaQueMuera = 2,
}

public static class MobsFixTipoValues
{
    public static readonly int[] Allowed = [-1, 0, 1, 2];

    public static bool IsAllowed(int tipo) =>
        tipo is -1 or 0 or 1 or 2;

    public static string DisplayName(int tipo) => tipo switch
    {
        -1 => "Siempre Aparece",
        0 => "Aparece cuando no está en pelea",
        1 => "Aparece solo 1 vez",
        2 => "Aparece hasta morir",
        _ => $"tipo {tipo}",
    };
}
