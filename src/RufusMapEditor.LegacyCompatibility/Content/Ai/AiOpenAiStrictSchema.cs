using System.Text.Json.Nodes;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4B — adapts AI.3 JSON Schemas for OpenAI Responses API Structured Outputs (strict).
/// Strips meta keys and ensures every property is listed in <c>required</c> (OpenAI strict rule).
/// </summary>
public static class AiOpenAiStrictSchema
{
    public static (string Name, JsonObject Schema) ForAction(AiCreativeAction action)
    {
        var root = JsonNode.Parse(AiJsonSchemas.ForAction(action)) as JsonObject
            ?? throw new InvalidOperationException("Schema inválido.");

        var name = root["title"]?.GetValue<string>() ?? action.ToString();
        root.Remove("$schema");
        root.Remove("title");
        EnforceStrictObject(root);
        return (name, root);
    }

    private static void EnforceStrictObject(JsonObject obj)
    {
        obj["additionalProperties"] = false;
        if (obj["properties"] is not JsonObject props)
            return;

        // OpenAI strict: every key under properties must appear in required.
        var required = new JsonArray();
        foreach (var key in props.Select(p => p.Key))
            required.Add(key);
        obj["required"] = required;

        foreach (var prop in props)
        {
            if (prop.Value is JsonObject child)
            {
                if (child["type"]?.GetValue<string>() == "object"
                    || child["properties"] is not null)
                    EnforceStrictObject(child);
                else if (child["type"]?.GetValue<string>() == "array"
                         && child["items"] is JsonObject items
                         && (items["type"]?.GetValue<string>() == "object" || items["properties"] is not null))
                    EnforceStrictObject(items);
            }
        }
    }
}
