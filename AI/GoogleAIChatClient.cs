using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;

using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using AIChatResponse = Microsoft.Extensions.AI.ChatResponse;
using AIChatResponseUpdate = Microsoft.Extensions.AI.ChatResponseUpdate;
using AIChatOptions = Microsoft.Extensions.AI.ChatOptions;
using AIChatClientMetadata = Microsoft.Extensions.AI.ChatClientMetadata;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI;

/// <summary>
/// IChatClient wrapper around Mscc.GenerativeAI (Google Gemini).
///
/// Translates Microsoft.Extensions.AI calls into GenerativeModel calls,
/// including full function-calling support: tools attached via
/// <see cref="AIChatOptions.Tools"/> are converted into Gemini
/// <see cref="Tool"/>/<see cref="FunctionDeclaration"/> objects, and any
/// function-call parts the model returns are surfaced back as
/// <see cref="FunctionCallContent"/> so the calling tool loop can dispatch
/// them. Prior assistant function calls and tool results are translated back
/// into Gemini functionCall / functionResponse parts so multi-turn tool
/// conversations work.
/// </summary>
public sealed class GoogleAIChatClient : IChatClient
{
    private readonly string _apiKey;
    private readonly string _modelName;

    public GoogleAIChatClient(string apiKey, string model)
    {
        _apiKey = apiKey;
        _modelName = model;
    }

    public AIChatClientMetadata Metadata => new(nameof(GoogleAIChatClient), null, _modelName);

    public async Task<AIChatResponse> GetResponseAsync(
        IEnumerable<AIChatMessage> chatMessages,
        AIChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var model = CreateModel(options);
        var request = BuildRequest(chatMessages, options);

        var response = await model.GenerateContent(request);

        var contents = new List<AIContent>();

        var text = SafeGetText(response);
        if (!string.IsNullOrEmpty(text))
            contents.Add(new TextContent(text));

        contents.AddRange(ExtractFunctionCalls(response));

        // Microsoft.Extensions.AI requires at least one content item on the
        // message; emit an empty text part when the model returned nothing
        // usable (e.g. a thinking model that spent its budget on reasoning).
        if (contents.Count == 0)
            contents.Add(new TextContent(string.Empty));

        var aiResponse = new AIChatResponse(new AIChatMessage(AIChatRole.Assistant, contents));

        // Surface token usage so callers can report accurate totals.
        // gemini-2.5-flash spends output tokens on reasoning ("thoughts"), so
        // fold ThoughtsTokenCount into the output total to avoid a misleadingly
        // low number.
        var usage = response?.UsageMetadata;
        if (usage is not null)
        {
            var outputTokens = (usage.CandidatesTokenCount ?? 0) + (usage.ThoughtsTokenCount ?? 0);
            aiResponse.Usage = new UsageDetails
            {
                InputTokenCount = usage.PromptTokenCount,
                OutputTokenCount = outputTokens,
                TotalTokenCount = usage.TotalTokenCount
            };
        }

        return aiResponse;
    }

    public async IAsyncEnumerable<AIChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AIChatMessage> chatMessages,
        AIChatOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = CreateModel(options);
        var request = BuildRequest(chatMessages, options);

        await foreach (var response in model.GenerateContentStream(request))
        {
            var text = SafeGetText(response);
            if (!string.IsNullOrEmpty(text))
            {
                yield return new AIChatResponseUpdate(AIChatRole.Assistant, text);
            }
        }
    }

    public object GetService(Type serviceType, object serviceKey = null)
        => serviceType == typeof(GoogleAIChatClient) ? this : null;

    public void Dispose() { /* GoogleAI client has no Dispose */ }

    /// <summary>
    /// Extracts assistant text from a Gemini response without relying on
    /// <c>GenerateContentResponse.Text</c>, which throws
    /// <see cref="ArgumentNullException"/> when a candidate's <c>Content</c> is
    /// null. Thinking models such as gemini-2.5-flash routinely return
    /// candidates with null/empty content (the output-token budget is spent on
    /// reasoning), so the library's convenience getter cannot be used safely.
    /// </summary>
    private static string SafeGetText(GenerateContentResponse response)
    {
        var parts = response?.Candidates?.FirstOrDefault()?.Content?.Parts;
        if (parts is null) return "";

        return string.Join(
            Environment.NewLine,
            parts.Where(p => p.Thought is null or false)
                 .Select(p => p.Text)
                 .Where(t => !string.IsNullOrEmpty(t)));
    }

    private GenerativeModel CreateModel(AIChatOptions options)
    {
        var googleAI = new GoogleAI(_apiKey);

        GenerationConfig config = null;
        if (options != null)
        {
            config = new GenerationConfig();
            if (options.Temperature.HasValue)
                config.Temperature = options.Temperature.Value;
            if (options.MaxOutputTokens.HasValue)
                config.MaxOutputTokens = options.MaxOutputTokens.Value;
            if (options.TopP.HasValue)
                config.TopP = options.TopP.Value;
            if (options.TopK.HasValue)
                config.TopK = options.TopK.Value;

            // Honor ChatResponseFormat.Json so Gemini returns strict JSON instead
            // of "JSON-ish" prose. Gemini rejects JSON mode combined with function
            // calling, so only request it when no tools are attached.
            if (options.ResponseFormat is ChatResponseFormatJson
                && (options.Tools is null || options.Tools.Count == 0))
            {
                config.ResponseMimeType = "application/json";
            }
        }

        return googleAI.GenerativeModel(
            model: _modelName,
            generationConfig: config);
    }

    private static GenerateContentRequest BuildRequest(
        IEnumerable<AIChatMessage> chatMessages,
        AIChatOptions options)
    {
        string systemInstruction = null;
        var contents = new List<Content>();

        // Gemini function responses must name the function they answer, but
        // FunctionResultContent only carries the call id. Track the call id ->
        // function name mapping from the preceding assistant function call so
        // tool results can be labelled correctly.
        var callIdToName = new Dictionary<string, string>();

        foreach (var msg in chatMessages)
        {
            if (msg.Role == AIChatRole.System)
            {
                if (!string.IsNullOrWhiteSpace(msg.Text))
                    systemInstruction = msg.Text;
                continue;
            }

            // Tool result messages -> Gemini functionResponse parts (role user).
            if (msg.Role == AIChatRole.Tool)
            {
                var resultParts = new List<IPart>();
                foreach (var result in msg.Contents.OfType<FunctionResultContent>())
                {
                    var name = callIdToName.TryGetValue(result.CallId, out var mapped)
                        ? mapped
                        : result.CallId;

                    resultParts.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Name = name,
                            Response = new Dictionary<string, object> { ["result"] = result.Result }
                        }
                    });
                }

                if (resultParts.Count > 0)
                    contents.Add(new Content(resultParts, Role.User));
                continue;
            }

            // Assistant messages that requested tool calls -> Gemini
            // functionCall parts (role model).
            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Count > 0)
            {
                var callParts = new List<IPart>();

                if (!string.IsNullOrWhiteSpace(msg.Text))
                    callParts.Add(new Part { Text = msg.Text });

                foreach (var call in functionCalls)
                {
                    callIdToName[call.CallId] = call.Name;
                    callParts.Add(new Part
                    {
                        FunctionCall = new FunctionCall
                        {
                            Id = call.CallId,
                            Name = call.Name,
                            Args = call.Arguments
                        }
                    });
                }

                contents.Add(new Content(callParts, Role.Model));
                continue;
            }

            // Gemini rejects content parts with no data ("required oneof field
            // 'data' must have one initialized field"), so skip empty/whitespace
            // messages instead of emitting an invalid empty part.
            if (string.IsNullOrWhiteSpace(msg.Text))
                continue;

            var plainRole = msg.Role == AIChatRole.User ? Role.User : Role.Model;
            contents.Add(new Content(msg.Text) { Role = plainRole });
        }

        var request = new GenerateContentRequest
        {
            Contents = contents
        };

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            request.SystemInstruction = new Content(systemInstruction) { Role = Role.User };
        }

        var tools = BuildTools(options);
        if (tools != null)
            request.Tools = tools;

        return request;
    }

    /// <summary>
    /// Converts the Microsoft.Extensions.AI tools attached to the request into
    /// Gemini <see cref="Tool"/>/<see cref="FunctionDeclaration"/> objects so
    /// the model can request function calls.
    /// </summary>
    private static Tools BuildTools(AIChatOptions options)
    {
        var aiTools = options?.Tools;
        if (aiTools is null || aiTools.Count == 0)
            return null;

        var declarations = new List<FunctionDeclaration>();
        foreach (var tool in aiTools)
        {
            if (tool is not AIFunction func)
                continue;

            declarations.Add(new FunctionDeclaration
            {
                Name = func.Name,
                Description = func.Description ?? string.Empty,
                ParametersJsonSchema = BuildParametersSchema(func.JsonSchema)
            });
        }

        if (declarations.Count == 0)
            return null;

        return new Tools { new Tool { FunctionDeclarations = declarations } };
    }

    /// <summary>
    /// Produces a JSON Schema object for a function's parameters, or null when
    /// the function takes no parameters (Gemini rejects an empty parameter
    /// schema, so it must be omitted entirely).
    /// </summary>
    private static object BuildParametersSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return null;

        if (!schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object ||
            !properties.EnumerateObject().Any())
        {
            return null;
        }

        return JsonNode.Parse(schema.GetRawText());
    }

    /// <summary>
    /// Extracts any function-call parts from a Gemini response and surfaces
    /// them as <see cref="FunctionCallContent"/> so the calling tool loop can
    /// dispatch them.
    /// </summary>
    private static IEnumerable<FunctionCallContent> ExtractFunctionCalls(GenerateContentResponse response)
    {
        var parts = response?.Candidates?.FirstOrDefault()?.Content?.Parts;
        if (parts is null)
            yield break;

        foreach (var part in parts)
        {
            var call = part.FunctionCall;
            if (call is null)
                continue;

            // Gemini may omit a call id; synthesize a stable one so the tool
            // result can be matched back to this call.
            var callId = !string.IsNullOrEmpty(call.Id)
                ? call.Id
                : $"{call.Name}_{Guid.NewGuid():N}";

            yield return new FunctionCallContent(callId, call.Name ?? string.Empty, ConvertArgs(call.Args));
        }
    }

    /// <summary>
    /// Normalizes the model's function-call arguments (returned as a JSON
    /// object) into the dictionary shape expected by the tool dispatcher.
    /// </summary>
    private static IDictionary<string, object> ConvertArgs(object args)
    {
        switch (args)
        {
            case null:
                return null;
            case JsonElement je when je.ValueKind == JsonValueKind.Object:
                var dict = new Dictionary<string, object>();
                foreach (var prop in je.EnumerateObject())
                    dict[prop.Name] = prop.Value;
                return dict;
            case IDictionary<string, object> existing:
                return existing;
            default:
                return null;
        }
    }
}
