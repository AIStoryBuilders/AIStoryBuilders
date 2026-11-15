using System.Diagnostics;
using System.Text.Json;
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Settings;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.Llm;

public static class ProbeConstants
{
    /// <summary>Bumping this invalidates every verified model (plan §5.9).</summary>
    public const int Version = 1;
    public static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Reasoning models spend output tokens before they answer.</summary>
    public const int MaxOutputTokens = 2048;
}

/// <summary>The exact probe texts (Test-Data §5). The probe sends only this synthetic text, never story content (§9).</summary>
public static class ProbeFixtures
{
    public const string SystemPrompt = "You are being tested for tool use. Follow the instructions exactly.";
    public const string ConnectMessage = "Reply with OK.";

    public const string ToolName = "kb_read_page";
    public const string ToolDescription = "Reads one knowledge-base page and the pages it links to.";
    public const string ToolSchema = """{"type":"object","properties":{"path":{"type":"string","description":"Page path without .md"}},"required":["path"]}""";
    public const string ExpectedPath = "Characters/Freya";

    public const string ToolCallMessage = "Call the kb_read_page tool with path \"Characters/Freya\". Do not answer in text.";

    /// <summary>Sent once if the provider rejects a required tool choice (plan §5.9, R7).</summary>
    public const string ToolCallMessageAuto =
        "You must call the kb_read_page tool now, with path \"Characters/Freya\". Respond only with the tool call. Do not answer in text.";

    public const string GraphResult = """
        {"pages":[
         {"path":"Characters/Freya.md","markdown":"---\ntitle: Freya\ntype: character\n---\nFreya is the daughter of [[Odin]] and travels with [[Characters/Balt|Balt]]."},
         {"path":"Characters/Balt.md","markdown":"---\ntitle: Balt\ntype: character\n---\nBalt fears the ravens of [[Odin]] and protects [[Characters/Freya]]."},
         {"path":"Places/Asgard.md","markdown":"---\ntitle: Asgard\ntype: location\n---\nOdin rules here. See [[Characters/Freya]]."}
        ]}
        """;

    public const string GraphParseMessage =
        "Do not call tools. Using only the tool result above, return only JSON in the form {\"linksToOdin\":[...]} listing the paths of pages that contain a [[...]] link to Odin.";

    public static readonly string[] ExpectedLinksToOdin = ["Characters/Freya.md", "Characters/Balt.md"];

    public static AITool CreateTool() =>
        AIFunctionFactory.CreateDeclaration(ToolName, ToolDescription, JsonDocument.Parse(ToolSchema).RootElement.Clone());

    /// <summary>Trimmed, without a trailing ".md", lower case.</summary>
    public static string NormalizePath(string? path)
    {
        var p = (path ?? "").Trim();
        if (p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) p = p[..^3];
        return p.ToLowerInvariant();
    }
}

/// <summary>
/// The live capability probe (D9, plan §5.9): connect, then a forced tool call (A), then reading a small knowledge-graph tool
/// result (B). It builds its own client from the options it is given and never reads the saved settings.
/// </summary>
public sealed class ModelCapabilityProbe : IModelCapabilityProbe
{
    readonly Func<LlmOptions, IChatClient> _createClient;
    readonly bool _isBrowser;

    public ModelCapabilityProbe(HttpClient http, IHostCapabilities host)
        : this(o => ChatClientFactory.Create(o, http, host.IsBrowser), host.IsBrowser) { }

    /// <summary>For tests: supplies the provider client.</summary>
    public ModelCapabilityProbe(Func<LlmOptions, IChatClient> createClient, bool isBrowser = false)
    {
        _createClient = createClient;
        _isBrowser = isBrowser;
    }

    public TimeSpan StageTimeout { get; set; } = ProbeConstants.StageTimeout;

    public async Task<ModelProbeResult> ProbeAsync(LlmOptions options, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        ModelProbeResult Fail(ProbeStage stage, string message) => new(false, stage, message, clock.Elapsed);

        IChatClient client;
        try
        {
            client = new TemperatureFallbackChatClient(_createClient(options));
        }
        catch (ArgumentException ex)
        {
            return Fail(ProbeStage.Connect, ex.Message);
        }

        using (client)
        {
            var host = LlmDefaults.HostFor(options.Provider, options.Endpoint);
            var system = new ChatMessage(ChatRole.System, ProbeFixtures.SystemPrompt);

            // Connect
            try
            {
                await WithTimeout(t => client.GetResponseAsync([system, new ChatMessage(ChatRole.User, ProbeFixtures.ConnectMessage)],
                    new ChatOptions { MaxOutputTokens = ProbeConstants.MaxOutputTokens }, t), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Fail(ProbeStage.Connect, Describe(ex, host));
            }

            // Stage A: a forced tool call, without function invocation middleware.
            var tool = ProbeFixtures.CreateTool();
            var userA = new ChatMessage(ChatRole.User, ProbeFixtures.ToolCallMessage);
            ChatResponse responseA;
            try
            {
                responseA = await WithTimeout(t => client.GetResponseAsync([system, userA],
                    new ChatOptions { Tools = [tool], ToolMode = ChatToolMode.RequireAny, MaxOutputTokens = ProbeConstants.MaxOutputTokens }, t), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (ex is TimeoutException) return Fail(ProbeStage.ToolCall, Describe(ex, host));
                // Some providers reject a required tool choice: retry once with Auto and a firmer prompt (R7).
                userA = new ChatMessage(ChatRole.User, ProbeFixtures.ToolCallMessageAuto);
                try
                {
                    responseA = await WithTimeout(t => client.GetResponseAsync([system, userA],
                        new ChatOptions { Tools = [tool], ToolMode = ChatToolMode.Auto, MaxOutputTokens = ProbeConstants.MaxOutputTokens }, t), ct);
                }
                catch (Exception retry) when (retry is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    return Fail(ProbeStage.ToolCall, Describe(retry, host));
                }
            }

            var call = responseA.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().FirstOrDefault();
            if (call is null || call.Name != ProbeFixtures.ToolName)
                return Fail(ProbeStage.ToolCall, call is null
                    ? "The model answered in text and did not call kb_read_page."
                    : $"The model called {call.Name} instead of kb_read_page.");
            var path = call.Arguments?.TryGetValue("path", out var p) == true ? ArgumentText(p) : null;
            if (ProbeFixtures.NormalizePath(path) != ProbeFixtures.NormalizePath(ProbeFixtures.ExpectedPath))
                return Fail(ProbeStage.ToolCall, $"The model called kb_read_page with path \"{path}\" instead of \"{ProbeFixtures.ExpectedPath}\".");

            // Stage B: continue the history with the call and its result. The tool stays declared (Anthropic needs it).
            var history = new List<ChatMessage>
            {
                system,
                userA,
                new(ChatRole.Assistant, [call]),
                new(ChatRole.Tool, [new FunctionResultContent(call.CallId, ProbeFixtures.GraphResult)]),
                new(ChatRole.User, ProbeFixtures.GraphParseMessage),
            };
            ChatResponse responseB;
            try
            {
                responseB = await WithTimeout(t => client.GetResponseAsync(history,
                    new ChatOptions { Tools = [tool], ToolMode = ChatToolMode.Auto, MaxOutputTokens = ProbeConstants.MaxOutputTokens }, t), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Fail(ProbeStage.GraphParse, Describe(ex, host));
            }

            if (responseB.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any())
                return Fail(ProbeStage.GraphParse, "The model called a tool again instead of answering.");

            var links = ParseLinks(responseB.Text);
            if (links is null)
                return Fail(ProbeStage.GraphParse, "The model's answer was not the requested JSON.");
            var expected = ProbeFixtures.ExpectedLinksToOdin.Select(ProbeFixtures.NormalizePath).ToHashSet();
            if (!links.SetEquals(expected))
                return Fail(ProbeStage.GraphParse, $"The model listed [{string.Join(", ", links)}] as linking to Odin; the right answer is [{string.Join(", ", expected)}].");

            return new ModelProbeResult(true, null, null, clock.Elapsed);
        }
    }

    async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(StageTimeout);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The request timed out.");
        }
    }

    static string? ArgumentText(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement e => e.GetRawText(),
        _ => value.ToString(),
    };

    /// <summary>The normalized paths in {"linksToOdin":[...]}, after JSON repair; null when the answer isn't that JSON.</summary>
    internal static HashSet<string>? ParseLinks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(JsonRepairUtility.ExtractAndRepair(text));
            if (!doc.RootElement.TryGetProperty("linksToOdin", out var list) || list.ValueKind != JsonValueKind.Array) return null;
            return list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => ProbeFixtures.NormalizePath(e.GetString())).ToHashSet();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    string Describe(Exception ex, string? host) =>
        ex is TimeoutException ? "The request timed out." : AppSettingsService.DescribeError(ex, host, _isBrowser);
}
