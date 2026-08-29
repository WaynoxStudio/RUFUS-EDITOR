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
