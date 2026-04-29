using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI;

/// <summary>
/// Wraps <see cref="AIFunction"/> instances so their JSON schemas are accepted
/// by Google Gemini's stricter function-declaration validator.
///
/// Strategy: do NOT try to scrub the original schema in place. Instead,
/// REBUILD a minimal schema from scratch using only the safe subset Gemini
/// supports as <c>parametersJsonSchema</c>:
///   {
///     "type": "object",
///     "properties": { "name": { "type": "...", "description": "..." }, ... },
///     "required": [ ... ]
///   }
/// where each property type is one of: string | number | integer | boolean |
/// array (with primitive items) | object (empty). Unknown / nullable / oneOf /
/// referenced shapes degrade to "string".
/// </summary>
internal static class GeminiToolSanitizer
{
    public static IList<AITool> SanitizeForGemini(IList<AITool> tools)
    {
        if (tools == null || tools.Count == 0)
            return tools;

        var result = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            if (tool is AIFunction fn)
                result.Add(new GeminiSafeAIFunction(fn));
            else
                result.Add(tool);
        }
        return result;
    }

    private sealed class GeminiSafeAIFunction : AIFunction
    {
        private readonly AIFunction _inner;
        private readonly JsonElement _schema;

        public GeminiSafeAIFunction(AIFunction inner)
        {
            _inner = inner;
            _schema = BuildMinimalSchema(inner.JsonSchema);
        }

        public override string Name => _inner.Name;
        public override string Description => _inner.Description;
        public override JsonElement JsonSchema => _schema;
        public override JsonSerializerOptions JsonSerializerOptions => _inner.JsonSerializerOptions;

        protected override ValueTask<object> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
            => _inner.InvokeAsync(arguments, cancellationToken);
    }

    private static JsonElement BuildMinimalSchema(JsonElement source)
    {
        var root = new JsonObject { ["type"] = "object" };
        var properties = new JsonObject();
        var required = new JsonArray();

        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty("properties", out var propsEl) &&
            propsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in propsEl.EnumerateObject())
            {
                properties[prop.Name] = BuildMinimalProperty(prop.Value);
            }

            if (source.TryGetProperty("required", out var reqEl) &&
                reqEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in reqEl.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.String)
                    {
                        var name = r.GetString();
                        if (!string.IsNullOrEmpty(name) && properties.ContainsKey(name))
                            required.Add(name);
                    }
                }
            }
        }

        root["properties"] = properties;
        if (required.Count > 0)
            root["required"] = required;

        return JsonDocument.Parse(root.ToJsonString()).RootElement.Clone();
    }

    private static JsonObject BuildMinimalProperty(JsonElement prop)
    {
        var result = new JsonObject();

        // Description (optional, kept verbatim if a string).
        if (prop.ValueKind == JsonValueKind.Object &&
            prop.TryGetProperty("description", out var desc) &&
            desc.ValueKind == JsonValueKind.String)
        {
            result["description"] = desc.GetString();
        }

        var type = ResolvePrimitiveType(prop);
        result["type"] = type;

        // For arrays, supply a primitive items schema (Gemini requires items).
        if (type == "array")
        {
            string itemType = "string";
            if (prop.ValueKind == JsonValueKind.Object &&
                prop.TryGetProperty("items", out var itemsEl))
            {
                itemType = ResolvePrimitiveType(itemsEl);
                if (itemType == "array" || itemType == "object")
                    itemType = "string";
            }
            result["items"] = new JsonObject { ["type"] = itemType };
        }

        // Pass through enum values when they're all strings (Gemini supports this).
        if (type == "string" &&
            prop.ValueKind == JsonValueKind.Object &&
            prop.TryGetProperty("enum", out var enumEl) &&
            enumEl.ValueKind == JsonValueKind.Array)
        {
            var enumArr = new JsonArray();
            bool allStrings = true;
            foreach (var v in enumEl.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.String) { allStrings = false; break; }
                enumArr.Add(v.GetString());
            }
            if (allStrings && enumArr.Count > 0)
            {
                result["enum"] = enumArr;
                result["format"] = "enum";
            }
        }

        return result;
    }

    private static string ResolvePrimitiveType(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return "string";

        if (el.TryGetProperty("type", out var t))
        {
            if (t.ValueKind == JsonValueKind.String)
            {
                return Normalize(t.GetString());
            }
            if (t.ValueKind == JsonValueKind.Array)
            {
                // ["string","null"] -> "string"
                foreach (var v in t.EnumerateArray())
                {
                    if (v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrEmpty(s) && s != "null")
                            return Normalize(s);
                    }
                }
                return "string";
            }
        }

        // Fallback heuristics from common shapes.
        if (el.TryGetProperty("enum", out _)) return "string";
        if (el.TryGetProperty("items", out _)) return "array";
        if (el.TryGetProperty("properties", out _)) return "object";

        return "string";
    }

    private static string Normalize(string type) => type switch
    {
        "string" or "number" or "integer" or "boolean" or "array" or "object" => type,
        _ => "string"
    };
}
