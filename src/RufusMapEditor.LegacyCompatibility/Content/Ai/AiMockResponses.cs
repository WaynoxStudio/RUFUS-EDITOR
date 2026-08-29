namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.3 — development-only mock payloads for preview UI / tests.
/// Not real AI output. Never applied to NPC drafts automatically.
/// </summary>
public static class AiMockResponses
{
    public static string NamesJson { get; } =
        """
        {
          "nombres": [
            { "nombre": "Pico Tajo", "motivo": "Juego de palabras minero." },
            { "nombre": "Grava Sorda", "motivo": "Eco de túneles." },
            { "nombre": "Carboncillo", "motivo": "Oficio y hollín." }
          ]
        }
        """;

    public static string DialogueJson { get; } =
        """
        {
          "dialogo": {
            "texto": "Si vienes a mirar piedras, mira sin tocar. Si vienes a ayudar… bueno, eso ya es otra historia."
          }
        }
        """;

    public static string ConversationJson { get; } =
        """
        {
          "conversacion": {
            "textoNpc": "¿Otra vez por aquí? La salida sigue igual de cerrada, por si no lo habías notado.",
            "respuestasJugador": [
              { "texto": "Solo paso a ver cómo estás.", "tono": "neutral" },
              { "texto": "Puedo echarte una mano con eso.", "tono": "amable" },
              { "texto": "¿Cerrada? Qué conveniente…", "tono": "humoristico" }
            ]
          }
        }
        """;

    public static string ForAction(AiCreativeAction action) => action switch
    {
        AiCreativeAction.GenerarNombre => NamesJson,
        AiCreativeAction.GenerarDialogo => DialogueJson,
        AiCreativeAction.GenerarConversacion => ConversationJson,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    public static AiGenerationResult LoadValidated(AiCreativeAction action) =>
        AiResponseValidator.ParseAndValidate(action, ForAction(action));
}
