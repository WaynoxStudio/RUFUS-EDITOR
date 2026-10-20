namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Confirmed MySQL identifiers for estaticos.npcs_modelo (CONT.1 / CONT.2).</summary>
public static class NpcsModeloColumns
{
    public const string DefaultDatabase = "estaticos";
    public const string DefaultTable = "npcs_modelo";

    public const string Id = "id";
    public const string GfxId = "gfxID";
    public const string ScaleX = "scaleX";
    public const string ScaleY = "scaleY";
    public const string Sexo = "sexo";
    public const string Color1 = "color1";
    public const string Color2 = "color2";
    public const string Color3 = "color3";
    public const string Accesorios = "accesorios";
    public const string Foto = "foto";
    public const string Pregunta = "pregunta";
    public const string Ventas = "ventas";
    public const string Nombre = "nombre";
    public const string ObjetoCompra = "objetoCompra";
}

/// <summary>
/// CONTENT.NPC.SCALEX.OUT_OF_RANGE.FIX.1 / CONTENT.NPC.SCALE.AUTO_CLAMP.MAX.2 —
/// real MySQL <c>npcs_modelo.scaleX/scaleY</c>: tinyint signed, DEFAULT=100.
/// SQL accepts -128..127; UI auto-clamps values above <see cref="SqlMax"/> to 127.
/// </summary>
public static class NpcsModeloScale
{
    public const int SqlMin = -128;
    public const int SqlMax = 127;
    public const int Default = 100;

    public const string ErrorScaleX = "Escala X del NPC fuera del rango permitido.";
    public const string ErrorScaleY = "Escala Y del NPC fuera del rango permitido.";

    public const string AutoClampMaxNotice =
        "El tamaño máximo permitido es 127. Se ha ajustado automáticamente.";

    public static bool IsInSqlRange(int value) => value >= SqlMin && value <= SqlMax;

    /// <summary>CONTENT.NPC.SCALE.AUTO_CLAMP.MAX.2 — only clamps when value &gt; SqlMax.</summary>
    public static int ClampMax(int value, out bool wasClamped)
    {
        if (value > SqlMax)
        {
            wasClamped = true;
            return SqlMax;
        }

        wasClamped = false;
        return value;
    }

    public static bool TryValidate(int scaleX, int scaleY, out string? error)
    {
        if (!IsInSqlRange(scaleX))
        {
            error = ErrorScaleX;
            return false;
        }

        if (!IsInSqlRange(scaleY))
        {
            error = ErrorScaleY;
            return false;
        }

        error = null;
        return true;
    }

    public static IEnumerable<string> ValidateNpc(int npcId, string? nombre, int scaleX, int scaleY)
    {
        if (!IsInSqlRange(scaleX))
            yield return ErrorScaleX;
        if (!IsInSqlRange(scaleY))
            yield return ErrorScaleY;
        _ = npcId;
        _ = nombre;
    }
}

/// <summary>Confirmed MySQL identifiers for estaticos.npcs_ubicacion (CONT.2.1).</summary>
public static class NpcsUbicacionColumns
{
    public const string DefaultTable = "npcs_ubicacion";

    public const string Mapa = "mapa";
    public const string Celda = "celda";
    public const string Npc = "npc";
    public const string Orientacion = "orientacion";
    public const string Nombre = "nombre";
    public const string Condicion = "condicion";
}
