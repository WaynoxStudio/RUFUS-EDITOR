namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>ADMIN.UI.4B.2A — human-facing sex selector for npcs_modelo.sexo (0 = Hombre, 1 = Mujer).</summary>
public enum NpcSexoUi
{
    Hombre = 0,
    Mujer = 1,
}

/// <summary>ADMIN.UI.4B.2A — presentation helpers for Identidad (no model/schema changes).</summary>
public static class NpcIdentityUi
{
    public static NpcSexoUi SexoToUi(int sexo) => sexo == 0 ? NpcSexoUi.Hombre : NpcSexoUi.Mujer;

    public static int SexoFromUi(NpcSexoUi ui) => ui == NpcSexoUi.Hombre ? 0 : 1;

    public static bool HasUnequalScale(int scaleX, int scaleY) => scaleX != scaleY;

    public static string FormatUnequalScaleHint(int scaleX, int scaleY) =>
        $"Tamaño personalizado: X {scaleX} / Y {scaleY}";

    public static string FormatTamañoDisplay(int scaleX, int scaleY, bool userEditedTamaño) =>
        !HasUnequalScale(scaleX, scaleY) || userEditedTamaño
            ? scaleX.ToString()
            : "";

    public static (int ScaleX, int ScaleY) ApplyUniformTamaño(int value) => (value, value);

    public static bool TryParseTamaño(string? text, out int value) =>
        int.TryParse(text?.Trim(), out value);
}
