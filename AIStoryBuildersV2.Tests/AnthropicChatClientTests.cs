using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIStoryBuildersV2.Shared.Llm;
using Microsoft.Extensions.AI;
using RichardSzalay.MockHttp;

namespace AIStoryBuildersV2.Tests;

public class AnthropicChatClientTests
{
    const string Key = "sk-ant-api03-SECRET-KEY-1234567890";

    static string Sse(params object[] events)
    {
        var sb = new StringBuilder();
        foreach (var e in events)
        {
            var json = JsonSerializer.Serialize(e);
            var type = JsonNode.Parse(json)!["type"]!.GetValue<string>();
            sb.Append("event: ").Append(type).Append('\n').Append("data: ").Append(json).Append("\n\n");
        }
        return sb.ToString();
    }

    static readonly object[] TextAndToolStream =
    [
        new { type = "message_start", message = new { id = "msg_1", model = "claude-test", usage = new { input_tokens = 12 } } },
        new { type = "ping" },
        new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } },
        new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "Let me " } },
        new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "search." } },
        new { type = "content_block_stop", index = 0 },
        new { type = "content_block_start", index = 1, content_block = new { type = "tool_use", id = "toolu_1", name = "search_pages", input = new { } } },
        new { type = "content_block_delta", index = 1, delta = new { type = "input_json_delta", partial_json = "" } },
        new { type = "content_block_delta", index = 1, delta = new { type = "input_json_delta", partial_json = "{\"query\": \"ins" } },
        new { type = "content_block_delta", index = 1, delta = new { type = "input_json_delta", partial_json = "tall\", \"top\"" } },
        new { type = "content_block_delta", index = 1, delta = new { type = "input_json_delta", partial_json = ": 3}" } },
        new { type = "content_block_stop", index = 1 },
        new { type = "message_delta", delta = new { stop_reason = "tool_use" }, usage = new { output_tokens = 30 } },
        new { type = "message_stop" },
    ];

    static (AnthropicChatClient Client, MockHttpMessageHandler Mock, List<HttpRequestMessage> Requests, List<string> Bodies) Create(
        string sse, bool browserAccess = false, HttpStatusCode status = HttpStatusCode.OK, string? errorBody = null)
    {
        var mock = new MockHttpMessageHandler();
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        mock.When(HttpMethod.Post, AnthropicChatClient.Endpoint).Respond(req =>
        {
            requests.Add(req);
            bodies.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return status == HttpStatusCode.OK
                ? new HttpResponseMessage(status) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") }
                : new HttpResponseMessage(status) { Content = new StringContent(errorBody ?? "", Encoding.UTF8, "application/json") };
        });
        return (new AnthropicChatClient(mock.ToHttpClient(), Key, "claude-test", browserAccess), mock, requests, bodies);
    }

    [Fact]
    public async Task Streaming_EmitsTextDeltas_OneToolCall_AndTheStopReason()
    {
        var (client, _, _, _) = Create(Sse(TextAndToolStream));

        var updates = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]).ToListAsync();

        var texts = updates.SelectMany(u => u.Contents).OfType<TextContent>().Select(t => t.Text).Where(t => t.Length > 0).ToList();
        Assert.Equal(["Let me ", "search."], texts);

        var call = Assert.Single(updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>());
        Assert.Equal("toolu_1", call.CallId);
        Assert.Equal("search_pages", call.Name);
        Assert.Equal("install", ((JsonElement)call.Arguments!["query"]!).GetString());
        Assert.Equal(3, ((JsonElement)call.Arguments!["top"]!).GetInt32());

        Assert.Equal(ChatFinishReason.ToolCalls, updates.Last(u => u.FinishReason is not null).FinishReason);
        Assert.All(updates, u => Assert.Equal(ChatRole.Assistant, u.Role));
        Assert.Contains(updates, u => u.MessageId == "msg_1");

        var response = updates.ToChatResponse();
        Assert.Equal("Let me search.", response.Text);
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        Assert.Equal(12, response.Usage?.InputTokenCount);
        Assert.Equal(30, response.Usage?.OutputTokenCount);
    }

    [Theory]
    [InlineData("end_turn", "stop")]
    [InlineData("stop_sequence", "stop")]
    [InlineData("max_tokens", "length")]
    [InlineData("refusal", "content_filter")]
    [InlineData("pause_turn", "pause_turn")]
    public async Task StopReason_IsMapped(string stopReason, string expected)
    {
        var (client, _, _, _) = Create(Sse(
            new { type = "message_start", message = new { id = "m", model = "claude-test" } },
            new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "Hi" } },
            new { type = "content_block_stop", index = 0 },
            new { type = "message_delta", delta = new { stop_reason = stopReason } },
            new { type = "message_stop" }));

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(expected, response.FinishReason?.Value);
        Assert.Equal("Hi", response.Text);
    }

    [Fact]
    public async Task ToolCall_WithNoInput_HasEmptyArguments()
    {
        var (client, _, _, _) = Create(Sse(
            new { type = "content_block_start", index = 0, content_block = new { type = "tool_use", id = "t", name = "list_folder", input = new { } } },
            new { type = "content_block_stop", index = 0 },
            new { type = "message_delta", delta = new { stop_reason = "tool_use" } }));

        var updates = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]).ToListAsync();

        var call = Assert.Single(updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>());
        Assert.NotNull(call.Arguments);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public async Task StreamErrorEvent_Throws()
    {
        var (client, _, _, _) = Create(Sse(
            new { type = "message_start", message = new { id = "m" } },
            new { type = "error", error = new { type = "overloaded_error", message = "Overloaded" } }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
        Assert.Contains("Overloaded", ex.Message);
    }

    [Fact]
    public async Task Headers_WithoutBrowserAccess()
    {
        var (client, _, requests, _) = Create(Sse(new { type = "message_stop" }));

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var request = Assert.Single(requests);
        Assert.Equal([Key], request.Headers.GetValues("x-api-key"));
        Assert.Equal(["2023-06-01"], request.Headers.GetValues("anthropic-version"));
        Assert.False(request.Headers.Contains("anthropic-dangerous-direct-browser-access"));
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "text/event-stream");
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Headers_WithBrowserAccess()
    {
        var (client, _, requests, _) = Create(Sse(new { type = "message_stop" }), browserAccess: true);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(["true"], Assert.Single(requests).Headers.GetValues("anthropic-dangerous-direct-browser-access"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", "invalid x-api-key")]
    [InlineData(HttpStatusCode.BadRequest, """{"type":"error","error":{"type":"invalid_request_error","message":"messages: roles must alternate"}}""", "roles must alternate")]
    [InlineData(HttpStatusCode.InternalServerError, "upstream failure", "upstream failure")]
    public async Task ErrorResponses_ThrowHttpRequestException_WithoutTheKey(HttpStatusCode status, string body, string expected)
    {
        var (client, _, _, _) = Create("", status: status, errorBody: body);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(status, ex.StatusCode);
        Assert.Contains(expected, ex.Message);
        Assert.Contains(((int)status).ToString(), ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
        Assert.DoesNotContain("SECRET", ex.ToString());
    }

    [Fact]
    public async Task ErrorResponse_ThatEchoesTheKey_IsNotLeakedByDescribeError()
    {
        var (client, _, _, _) = Create("", status: HttpStatusCode.BadRequest, errorBody: $"bad key {Key}");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        var message = Shared.Settings.AppSettingsService.DescribeError(ex, "api.anthropic.com", false);
        Assert.DoesNotContain("SECRET-KEY-1234567890", message);
    }

    [Fact]
    public async Task Request_IsSentAsStreamingJson()
    {
        var (client, _, _, bodies) = Create(Sse(new { type = "message_stop" }));

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions { MaxOutputTokens = 1 });

        var body = JsonNode.Parse(Assert.Single(bodies))!;
        Assert.Equal("claude-test", (string?)body["model"]);
        Assert.Equal(1, (int)body["max_tokens"]!);
        Assert.True((bool)body["stream"]!);
    }

    // ------------------------------------------------------------ request mapping

    [Fact]
    public void BuildRequest_JoinsSystemMessages_AndDefaultsMaxTokens()
    {
        var body = AnthropicChatClient.BuildRequest(
            [new ChatMessage(ChatRole.System, "First."), new ChatMessage(ChatRole.User, "Hi"), new ChatMessage(ChatRole.System, "Second.")],
            new ChatOptions { Instructions = "Instructions." }, "claude-test", stream: false);

        Assert.Equal("Instructions.\n\nFirst.\n\nSecond.", (string?)body["system"]);
        Assert.Equal(AnthropicChatClient.DefaultMaxTokens, (int)body["max_tokens"]!);
        Assert.Null(body["stream"]);
        var messages = body["messages"]!.AsArray();
        var only = Assert.Single(messages);
        Assert.Equal("user", (string?)only!["role"]);
        Assert.Equal("Hi", (string?)only["content"]![0]!["text"]);
        Assert.Equal("text", (string?)only["content"]![0]!["type"]);
    }

    [Fact]
    public void BuildRequest_MapsToolUse_AndMergesAdjacentToolResults()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Search two things"),
            new(ChatRole.Assistant,
            [
                new TextContent("Searching."),
                new FunctionCallContent("call_1", "search_pages", new Dictionary<string, object?> { ["query"] = "install", ["top"] = 3 }),
                new FunctionCallContent("call_2", "read_page", new Dictionary<string, object?> { ["path"] = "Welcome.md" }),
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "[{\"path\":\"Install.md\"}]")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_2", new { path = "Welcome.md" })]),
            new(ChatRole.Assistant, "Done."),
            new(ChatRole.User, "Thanks"),
        };

        var body = AnthropicChatClient.BuildRequest(messages, null, "claude-test", stream: true);
        var turns = body["messages"]!.AsArray();

        Assert.Equal(["user", "assistant", "user", "assistant", "user"], turns.Select(t => (string)t!["role"]!));

        var assistant = turns[1]!["content"]!.AsArray();
        Assert.Equal(["text", "tool_use", "tool_use"], assistant.Select(b => (string)b!["type"]!));
        Assert.Equal("call_1", (string?)assistant[1]!["id"]);
        Assert.Equal("search_pages", (string?)assistant[1]!["name"]);
        Assert.Equal("install", (string?)assistant[1]!["input"]!["query"]);
        Assert.Equal(3, (int)assistant[1]!["input"]!["top"]!);

        var results = turns[2]!["content"]!.AsArray();
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("tool_result", (string?)r!["type"]));
        Assert.Equal("call_1", (string?)results[0]!["tool_use_id"]);
        Assert.Equal("[{\"path\":\"Install.md\"}]", (string?)results[0]!["content"]);
        Assert.Equal("call_2", (string?)results[1]!["tool_use_id"]);
        Assert.Equal("Welcome.md", (string?)JsonNode.Parse((string)results[1]!["content"]!)!["path"]);
    }

    [Fact]
    public void BuildRequest_FailedToolResult_IsMarkedAsError()
    {
        var body = AnthropicChatClient.BuildRequest(
        [
            new(ChatRole.User, "x"),
            new(ChatRole.Assistant, [new FunctionCallContent("c", "read_page", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("c", null) { Exception = new InvalidOperationException("boom") }]),
        ], null, "m", stream: true);

        var result = body["messages"]![2]!["content"]![0]!;
        Assert.True((bool)result["is_error"]!);
        Assert.Contains("boom", (string?)result["content"]);
    }

    [Fact]
    public void BuildRequest_MapsToolDeclarations()
    {
        var tool = AIFunctionFactory.Create(([System.ComponentModel.Description("Keywords")] string query, int top = 8) => "", "search_pages", "Searches pages.");

        var body = AnthropicChatClient.BuildRequest([new ChatMessage(ChatRole.User, "x")],
            new ChatOptions { Tools = [tool], MaxOutputTokens = 100, Temperature = 0.5f, StopSequences = ["END"], ModelId = "override" }, "m", stream: true);

        var t = Assert.Single(body["tools"]!.AsArray())!;
        Assert.Equal("search_pages", (string?)t["name"]);
        Assert.Equal("Searches pages.", (string?)t["description"]);
        var schema = t["input_schema"]!;
        Assert.Equal("object", (string?)schema["type"]);
        Assert.Equal("string", (string?)schema["properties"]!["query"]!["type"]);
        Assert.Equal("Keywords", (string?)schema["properties"]!["query"]!["description"]);
        Assert.Contains("query", schema["required"]!.AsArray().Select(r => (string?)r));
        Assert.Null(schema["$schema"]);

        Assert.Equal(100, (int)body["max_tokens"]!);
        Assert.Equal("override", (string?)body["model"]);
        Assert.Equal(0.5f, (float)body["temperature"]!);
        Assert.Equal("END", (string?)body["stop_sequences"]![0]);
    }

    [Fact]
    public void BuildRequest_SkipsEmptyText_AndOmitsSystemWhenNone()
    {
        var body = AnthropicChatClient.BuildRequest([new ChatMessage(ChatRole.User, [new TextContent(""), new TextContent("x")])], null, "m", stream: true);
        Assert.Null(body["system"]);
        Assert.Null(body["tools"]);
        Assert.Single(body["messages"]![0]!["content"]!.AsArray());
    }

    [Fact]
    public void Metadata_IsExposed()
    {
        var (client, _, _, _) = Create("");
        var metadata = client.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal("anthropic", metadata.ProviderName);
        Assert.Equal("claude-test", metadata.DefaultModelId);
    }
}
