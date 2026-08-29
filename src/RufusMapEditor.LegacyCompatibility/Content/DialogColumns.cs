namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Confirmed columns for estaticos.npc_preguntas (CONT.1 / CONT.3).</summary>
public static class NpcPreguntasColumns
{
    public const string DefaultTable = "npc_preguntas";
    public const string Id = "id";
    public const string Respuestas = "respuestas";
    public const string Params = "params";
    public const string Alternos = "alternos";
}

/// <summary>Confirmed columns for estaticos.npc_respuestas (CONT.1 / CONT.3).</summary>
public static class NpcRespuestasColumns
{
    public const string DefaultTable = "npc_respuestas";
    public const string Orden = "orden";
    public const string Id = "id";
    public const string Accion = "accion";
    public const string Args = "args";
    public const string Condicion = "condicion";
}

/// <summary>Action codes demonstrated in CONT.1 (no invented meanings).</summary>
public static class DialogActionCodes
{
    public const int Teleport = 0;
    public const int GotoQuestion = 1;
    public const int StartQuest = 44;
}
