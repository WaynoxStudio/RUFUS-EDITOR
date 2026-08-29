namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.4A — controlled wire actions for Editor → RUFUS backend. Not free text.</summary>
public static class AiBackendWireActions
{
    public const string GenerateName = "generate_name";
    public const string GenerateDialogue = "generate_dialogue";
    public const string GenerateConversation = "generate_conversation";

    public static string ToWire(AiCreativeAction action) => action switch
    {
        AiCreativeAction.GenerarNombre => GenerateName,
        AiCreativeAction.GenerarDialogo => GenerateDialogue,
        AiCreativeAction.GenerarConversacion => GenerateConversation,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Acción creativa no soportada.")
    };

    public static bool TryParse(string? wire, out AiCreativeAction action)
    {
        action = default;
        if (string.IsNullOrWhiteSpace(wire))
            return false;
        switch (wire.Trim())
        {
            case GenerateName:
                action = AiCreativeAction.GenerarNombre;
                return true;
            case GenerateDialogue:
                action = AiCreativeAction.GenerarDialogo;
                return true;
            case GenerateConversation:
                action = AiCreativeAction.GenerarConversacion;
                return true;
            default:
                return false;
        }
    }

    public static bool IsKnown(string? wire) => TryParse(wire, out _);
}
