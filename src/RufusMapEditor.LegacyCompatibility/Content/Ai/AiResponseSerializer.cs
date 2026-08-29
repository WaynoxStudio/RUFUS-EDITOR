using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.3 — UTF-8 JSON (de)serialization for structured AI responses. No manual Split/Regex parsing.</summary>
public static class AiResponseSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static byte[] SerializeUtf8Bytes<T>(T value) =>
        Encoding.UTF8.GetBytes(Serialize(value));

    public static bool TryDeserialize<T>(string json, out T? value, out string? error)
    {
        value = default;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "JSON vacío.";
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, Options);
            if (value is null)
            {
                error = "JSON deserializado a null.";
                return false;
            }
            return true;
        }
        catch (JsonException ex)
        {
            error = "JSON inválido o propiedades inesperadas: " + ex.Message;
            return false;
        }
    }

    /// <summary>Deserializes according to the creative action. Rejects wrong payload shapes.</summary>
    public static bool TryDeserializeForAction(
        AiCreativeAction action,
        string json,
        out object? payload,
        out string? error)
    {
        payload = null;
        error = null;
        return action switch
        {
            AiCreativeAction.GenerarNombre => Wrap(
                TryDeserialize<AiNameGenerationResponse>(json, out var n, out error), n, out payload),
            AiCreativeAction.GenerarDialogo => Wrap(
                TryDeserialize<AiDialogueGenerationResponse>(json, out var d, out error), d, out payload),
            AiCreativeAction.GenerarConversacion => Wrap(
                TryDeserialize<AiConversationGenerationResponse>(json, out var c, out error), c, out payload),
            _ => FailUnknown(out error)
        };
    }

    private static bool Wrap<T>(bool ok, T? value, out object? payload) where T : class
    {
        payload = ok ? value : null;
        return ok;
    }

    private static bool FailUnknown(out string? error)
    {
        error = "Acción creativa desconocida.";
        return false;
    }
}
