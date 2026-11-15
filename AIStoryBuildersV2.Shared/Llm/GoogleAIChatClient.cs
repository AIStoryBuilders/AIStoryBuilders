using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.Llm;

/// <summary>
/// Gemini over its REST API, from AIStoryBuildersOnline (the Mscc.GenerativeAI SDK does not run in WASM). Changes for V2:
/// the key goes in the <c>x-goog-api-key</c> header, never the URL (D8); tools are declared and function calls and
/// results round-trip, keeping Gemini's thought signatures (R17); <c>MaxOutputTokens</c> and <c>ToolMode</c> are sent.
/// Streaming is a single non-streaming call yielded as one update, as in Online.
/// </summary>
public sealed class GoogleAIChatClient(HttpClient http, string apiKey, string model) : IChatClient
{
    public const string BaseUrl = "https://" + LlmDefaults.GoogleAIHost + "/v1beta";
    const string ThoughtSignatureKey = "gemini.thoughtSignature";

    public ChatClientMetadata Metadata => new(nameof(GoogleAIChatClient), new Uri(BaseUrl), model);

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/models/{Uri.EscapeDataString(model)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(BuildRequestBody(messages, options).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini generateContent failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Trim(raw)}", null, response.StatusCode);

        return ParseResponse(raw);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var full = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in full.ToChatResponseUpdates()) yield return update;
    }

    internal static JsonObject BuildRequestBody(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var system = new StringBuilder();
        var contents = new JsonArray();
        // Gemini matches function responses by name; remember each call id's function name.
        var callNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (system.Length > 0) system.Append("\n\n");
                system.Append(message.Text);
                continue;
            }

            var parts = new JsonArray();
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent text when !string.IsNullOrEmpty(text.Text):
                        parts.Add(new JsonObject { ["text"] = text.Text });
                        break;
                    case FunctionCallContent call:
                        callNames[call.CallId] = call.Name;
                        var part = new JsonObject
                        {
                            ["functionCall"] = new JsonObject
                            {
                                ["name"] = call.Name,
                                ["args"] = JsonSerializer.SerializeToNode(call.Arguments ?? new Dictionary<string, object?>()),
                            },
                        };
                        if (call.AdditionalProperties?.TryGetValue(ThoughtSignatureKey, out var signature) == true && signature is string sig)
                            part["thoughtSignature"] = sig;
                        parts.Add(part);
                        break;
                    case FunctionResultContent result:
                        parts.Add(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = callNames.GetValueOrDefault(result.CallId, result.CallId),
                                ["response"] = ToResponseObject(result.Result),
                            },
                        });
                        break;
                }
            }
            if (parts.Count == 0) continue;

            // Tool results are sent as a user turn; assistant turns are "model".
            var role = message.Role == ChatRole.Assistant ? "model" : "user";
            contents.Add(new JsonObject { ["role"] = role, ["parts"] = parts });
        }

        // Gemini needs at least one content; a system-only request becomes a user turn (Online).
        if (contents.Count == 0)
        {
            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = system.Length > 0 ? system.ToString() : "Hello." }) });
            system.Clear();
        }

        var body = new JsonObject { ["contents"] = contents };
        if (system.Length > 0) body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system.ToString() }) };

        var functions = options?.Tools?.OfType<AIFunctionDeclaration>().ToList() ?? [];
        if (functions.Count > 0)
        {
            var declarations = new JsonArray();
            foreach (var f in GeminiToolSanitizer.SanitizeForGemini(functions.Cast<AITool>().ToList()).OfType<AIFunctionDeclaration>())
            {
                declarations.Add(new JsonObject
                {
                    ["name"] = f.Name,
                    ["description"] = f.Description,
                    ["parametersJsonSchema"] = JsonNode.Parse(f.JsonSchema.GetRawText()),
                });
            }
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations });

            var mode = options!.ToolMode switch
            {
                RequiredChatToolMode => "ANY",
                NoneChatToolMode => "NONE",
                _ => "AUTO",
            };
            var config = new JsonObject { ["mode"] = mode };
            if (options.ToolMode is RequiredChatToolMode { RequiredFunctionName: { } only }) config["allowedFunctionNames"] = new JsonArray(only);
            body["toolConfig"] = new JsonObject { ["functionCallingConfig"] = config };
        }

        var generation = new JsonObject();
        if (options?.Temperature is { } temperature) generation["temperature"] = temperature;
        if (options?.TopP is { } topP) generation["topP"] = topP;
        if (options?.MaxOutputTokens is { } max) generation["maxOutputTokens"] = max;
        if (options?.ResponseFormat is ChatResponseFormatJson && functions.Count == 0) generation["responseMimeType"] = "application/json";
        if (generation.Count > 0) body["generationConfig"] = generation;
        return body;
    }

    static JsonNode ToResponseObject(object? result)
    {
        JsonNode? node = result switch
        {
            null => null,
            JsonElement e => JsonNode.Parse(e.GetRawText()),
            JsonNode n => n.DeepClone(),
            string s => TryParse(s) ?? JsonValue.Create(s),
            _ => JsonSerializer.SerializeToNode(result),
        };
        // functionResponse.response must be an object.
        return node is JsonObject obj ? obj : new JsonObject { ["result"] = node };

        static JsonNode? TryParse(string s)
        {
            try { return JsonNode.Parse(s); }
            catch (JsonException) { return null; }
        }
    }

    internal static ChatResponse ParseResponse(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
        {
            var reason = root.TryGetProperty("promptFeedback", out var pf) && pf.TryGetProperty("blockReason", out var br) ? br.GetString() : null;
            throw new InvalidOperationException("Gemini returned no candidates" + (reason is null ? "." : $" (blockReason={reason})."));
        }

        var message = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[]);
        var first = candidates[0];
        if (first.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            var callIndex = 0;
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    message.Contents.Add(new TextContent(text.GetString()));
                }
                else if (part.TryGetProperty("functionCall", out var call))
                {
                    var name = call.GetProperty("name").GetString() ?? "";
                    var id = call.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String ? idProp.GetString()! : $"gemini_call_{callIndex}";
                    callIndex++;
                    var args = call.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object
                        ? a.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())
                        : new Dictionary<string, object?>();
                    var fc = new FunctionCallContent(id, name, args);
                    if (part.TryGetProperty("thoughtSignature", out var sig) && sig.ValueKind == JsonValueKind.String)
                        (fc.AdditionalProperties ??= [])[ThoughtSignatureKey] = sig.GetString();
                    message.Contents.Add(fc);
                }
            }
        }

        var response = new ChatResponse(message)
        {
            ModelId = root.TryGetProperty("modelVersion", out var mv) ? mv.GetString() : null,
            FinishReason = message.Contents.OfType<FunctionCallContent>().Any() ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
        };
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = Long(usage, "promptTokenCount"),
                OutputTokenCount = Long(usage, "candidatesTokenCount"),
                TotalTokenCount = Long(usage, "totalTokenCount"),
            };
        }
        return response;
    }

    static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;

    static string Trim(string raw) => raw.Length > 400 ? raw[..400] + "…" : raw;

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
