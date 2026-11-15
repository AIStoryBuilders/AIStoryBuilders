using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.Llm;

/// <summary>
/// A hand-written Anthropic Messages API client (D9) with streaming and tool use. In the browser it sends the
/// CORS opt-in header <c>anthropic-dangerous-direct-browser-access</c>.
/// </summary>
public sealed class AnthropicChatClient(HttpClient http, string apiKey, string model, bool browserAccess) : IChatClient
{
    public const string Endpoint = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";
    public const int DefaultMaxTokens = 4096;

    static readonly HttpRequestOptionsKey<bool> BrowserStreaming = new("WebAssemblyEnableStreamingResponse");

    readonly ChatClientMetadata _metadata = new("anthropic", new Uri(Endpoint), model);

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
            updates.Add(update);
        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequest(messages, options, model, stream: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (browserAccess)
        {
            request.Headers.Add("anthropic-dangerous-direct-browser-access", "true");
            request.Options.Set(BrowserStreaming, true);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Anthropic API error {(int)response.StatusCode}: {ErrorMessage(errorBody)}", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var state = new StreamState { ModelId = model };
        var data = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (line.Length == 0)
            {
                if (data.Length == 0) continue;
                var json = data.ToString();
                data.Clear();
                foreach (var update in HandleEvent(json, state))
                    yield return update;
                continue;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart());
            }
            // "event:" lines are ignored; the JSON payload carries its own type.
        }
        if (data.Length > 0)
        {
            foreach (var update in HandleEvent(data.ToString(), state))
                yield return update;
        }
    }

    sealed class ToolBlock(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public StringBuilder Json { get; } = new();
    }

    sealed class StreamState
    {
        public string? MessageId { get; set; }
        public string? ModelId { get; set; }
        public Dictionary<int, ToolBlock> Blocks { get; } = [];
    }

    static List<ChatResponseUpdate> HandleEvent(string json, StreamState state)
    {
        var blocks = state.Blocks;
        var result = new List<ChatResponseUpdate>();
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return result; }
        if (node is null) return result;

        ChatResponseUpdate New() => new()
        {
            Role = ChatRole.Assistant,
            MessageId = state.MessageId,
            ResponseId = state.MessageId,
            ModelId = state.ModelId,
        };

        switch ((string?)node["type"])
        {
            case "message_start":
                state.MessageId = (string?)node["message"]?["id"];
                state.ModelId = (string?)node["message"]?["model"] ?? state.ModelId;
                if (node["message"]?["usage"] is JsonObject startUsage)
                {
                    var u = New();
                    u.Contents.Add(new UsageContent(new UsageDetails { InputTokenCount = (long?)startUsage["input_tokens"] }));
                    result.Add(u);
                }
                break;

            case "content_block_start":
            {
                var index = (int?)node["index"] ?? 0;
                var block = node["content_block"];
                if ((string?)block?["type"] == "tool_use")
                    blocks[index] = new ToolBlock((string?)block["id"] ?? Guid.NewGuid().ToString("N"), (string?)block["name"] ?? "");
                else if ((string?)block?["type"] == "text" && (string?)block["text"] is { Length: > 0 } initial)
                {
                    var u = New();
                    u.Contents.Add(new TextContent(initial));
                    result.Add(u);
                }
                break;
            }

            case "content_block_delta":
            {
                var index = (int?)node["index"] ?? 0;
                var delta = node["delta"];
                switch ((string?)delta?["type"])
                {
                    case "text_delta":
                        var u = New();
                        u.Contents.Add(new TextContent((string?)delta["text"] ?? ""));
                        result.Add(u);
                        break;
                    case "input_json_delta":
                        if (blocks.TryGetValue(index, out var tb)) tb.Json.Append((string?)delta["partial_json"]);
                        break;
                }
                break;
            }

            case "content_block_stop":
            {
                var index = (int?)node["index"] ?? 0;
                if (blocks.Remove(index, out var tb))
                {
                    var u = New();
                    u.Contents.Add(new FunctionCallContent(tb.Id, tb.Name, ParseArguments(tb.Json.ToString())));
                    result.Add(u);
                }
                break;
            }

            case "message_delta":
            {
                var u = New();
                u.FinishReason = MapStopReason((string?)node["delta"]?["stop_reason"]);
                if (node["usage"] is JsonObject usage)
                    u.Contents.Add(new UsageContent(new UsageDetails { OutputTokenCount = (long?)usage["output_tokens"] }));
                result.Add(u);
                break;
            }

            case "error":
                throw new HttpRequestException($"Anthropic API error: {(string?)node["error"]?["message"] ?? "unknown error"}");
        }
        return result;
    }

    static Dictionary<string, object?> ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var args = new Dictionary<string, object?>();
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject()) args[p.Name] = p.Value.Clone();
            return args;
        }
        catch (JsonException) { return []; }
    }

    static ChatFinishReason? MapStopReason(string? reason) => reason switch
    {
        null => null,
        "end_turn" or "stop_sequence" => ChatFinishReason.Stop,
        "max_tokens" => ChatFinishReason.Length,
        "tool_use" => ChatFinishReason.ToolCalls,
        "refusal" => ChatFinishReason.ContentFilter,
        _ => new ChatFinishReason(reason),
    };

    static string ErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            if ((string?)node?["error"]?["message"] is { } message) return message;
        }
        catch (JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }

    /// <summary>Maps MEAI messages and options to an Anthropic Messages API request (§5.8).</summary>
    internal static JsonObject BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, string model, bool stream)
    {
        var system = new List<string>();
        if (!string.IsNullOrWhiteSpace(options?.Instructions)) system.Add(options.Instructions);
        var turns = new List<(string Role, JsonArray Content)>();

        void Append(string role, JsonNode block)
        {
            if (turns.Count > 0 && turns[^1].Role == role) turns[^1].Content.Add(block);
            else turns.Add((role, new JsonArray { block }));
        }

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                var text = string.Concat(message.Contents.OfType<TextContent>().Select(t => t.Text));
                if (text.Length > 0) system.Add(text);
                continue;
            }

            var role = message.Role == ChatRole.Assistant ? "assistant" : "user";
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 } t:
                        Append(role, new JsonObject { ["type"] = "text", ["text"] = t.Text });
                        break;
                    case FunctionCallContent call:
                        var input = new JsonObject();
                        if (call.Arguments is not null)
                            foreach (var (k, v) in call.Arguments)
                                input[k] = v is null ? null : JsonSerializer.SerializeToNode(v, AIJsonUtilities.DefaultOptions);
                        Append("assistant", new JsonObject { ["type"] = "tool_use", ["id"] = call.CallId, ["name"] = call.Name, ["input"] = input });
                        break;
                    case FunctionResultContent result:
                        var block = new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = result.CallId,
                            ["content"] = ResultText(result),
                        };
                        if (result.Exception is not null) block["is_error"] = true;
                        Append("user", block);
                        break;
                }
            }
        }

        var body = new JsonObject
        {
            ["model"] = options?.ModelId ?? model,
            ["max_tokens"] = options?.MaxOutputTokens ?? DefaultMaxTokens,
            ["messages"] = new JsonArray(turns.Select(t => (JsonNode)new JsonObject { ["role"] = t.Role, ["content"] = t.Content }).ToArray()),
        };
        if (stream) body["stream"] = true;
        if (system.Count > 0) body["system"] = string.Join("\n\n", system);
        if (options?.Temperature is { } temp) body["temperature"] = temp;
        if (options?.StopSequences is { Count: > 0 } stops) body["stop_sequences"] = new JsonArray(stops.Select(s => (JsonNode)s).ToArray());

        var tools = options?.Tools?.OfType<AIFunctionDeclaration>().ToList();
        if (tools is { Count: > 0 })
        {
            body["tools"] = new JsonArray(tools.Select(t =>
            {
                var schema = JsonNode.Parse(t.JsonSchema.GetRawText()) as JsonObject ?? new JsonObject();
                schema.Remove("$schema");
                schema.Remove("title");
                if (!schema.ContainsKey("type")) schema["type"] = "object";
                return (JsonNode)new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = schema,
                };
            }).ToArray());
        }
        return body;
    }

    static string ResultText(FunctionResultContent result)
    {
        if (result.Exception is not null) return "Error: " + result.Exception.Message;
        return result.Result switch
        {
            null => "",
            string s => s,
            JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString() ?? "",
            var other => JsonSerializer.Serialize(other, AIJsonUtilities.DefaultOptions),
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata) ? _metadata
        : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() { }
}
