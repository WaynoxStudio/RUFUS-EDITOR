namespace RufusMapEditor.LegacyCompatibility.Content;

public static class MisionesColumns
{
    public const string DefaultTable = "misiones";
    public const string Id = "id";
    public const string Nombre = "nombre";
    public const string Etapas = "etapas";
    public const string PregDarMision = "pregDarMision";
    public const string PregMisCompletada = "pregMisCompletada";
    public const string PregMisIncompleta = "pregMisIncompleta";
    public const string PuedeRepetirse = "puedeRepetirse";
}

public static class MisionEtapasColumns
{
    public const string DefaultTable = "mision_etapas";
    public const string Id = "id";
    public const string Nombre = "nombre";
    public const string Descripcion = "descripcion";
    public const string Recompensas = "recompensas";
    public const string Objetivos = "objetivos";
    public const string VariosObj = "variosobj";
}

public static class MisionObjetivosColumns
{
    public const string DefaultTable = "mision_objetivos";
    public const string Id = "id";
    public const string Tipo = "tipo";
    public const string Args = "args";
    public const string Detalle = "detalle";
    public const string EsAlHablar = "esalHablar";
    public const string EsOculto = "esOculto";
    public const string Condicion = "condicion";
}

