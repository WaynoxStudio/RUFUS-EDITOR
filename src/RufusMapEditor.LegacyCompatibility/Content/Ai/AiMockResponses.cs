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
            "textoNpc": "Hay algo raro cerca de Astrub. Quiero que vayas al lugar que te indico y me digas qué ves.",
            "respuestasJugador": [
              { "texto": "De acuerdo, iré a mirar.", "tono": "neutral" }
            ],
            "textoIncompleta": "Todavía no has llegado al sitio. Vuelve cuando lo hayas visto con tus propios ojos.",
            "textoCompletada": "Bien. Ya has estado allí. Cuéntame qué has visto… sin inventarte dragones, por favor."
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
