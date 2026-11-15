using System.Text.Json;
using AIStoryBuildersV2.Shared.Llm;
using Microsoft.Extensions.AI;
using RichardSzalay.MockHttp;

namespace AIStoryBuildersV2.Tests;

/// <summary>D8: the Gemini key goes in a header, never the URL; tools and function calls round-trip.</summary>
public class GoogleAIChatClientTests
{
    const string Key = "AIzaTestKey1234567890";
    const string Url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent";

    [Fact]
    public async Task Sends_the_key_in_the_header_and_not_the_url()
    {
        var mock = new MockHttpMessageHandler();
        HttpRequestMessage? seen = null;
        mock.When(HttpMethod.Post, Url).With(r => { seen = r; return true; })
            .Respond("application/json", """{"candidates":[{"content":{"parts":[{"text":"OK"}]}}]}""");

        var client = new GoogleAIChatClient(mock.ToHttpClient(), Key, "gemini-test");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with OK.")]);

        Assert.Equal("OK", response.Text);
        Assert.NotNull(seen);
        Assert.DoesNotContain(Key, seen!.RequestUri!.ToString());
        Assert.Equal(Key, seen.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public void Declares_sanitized_tools_and_maps_tool_mode()
    {
        var tool = ProbeFixtures.CreateTool();
        var body = GoogleAIChatClient.BuildRequestBody(
            [new ChatMessage(ChatRole.System, "sys"), new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { Tools = [tool], ToolMode = ChatToolMode.RequireAny, MaxOutputTokens = 2048 });

        var decl = body["tools"]![0]!["functionDeclarations"]![0]!;
        Assert.Equal("kb_read_page", decl["name"]!.GetValue<string>());
        Assert.Equal("object", decl["parametersJsonSchema"]!["type"]!.GetValue<string>());
        Assert.Equal("ANY", body["toolConfig"]!["functionCallingConfig"]!["mode"]!.GetValue<string>());
        Assert.Equal(2048, body["generationConfig"]!["maxOutputTokens"]!.GetValue<int>());
        Assert.Equal("sys", body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Function_calls_and_results_round_trip_with_thought_signatures()
    {
        var response = GoogleAIChatClient.ParseResponse("""
            {"candidates":[{"content":{"parts":[{"functionCall":{"name":"kb_read_page","args":{"path":"Characters/Freya"}},"thoughtSignature":"sig123"}]}}]}
            """);
        var call = response.Messages.Single().Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("kb_read_page", call.Name);
        Assert.Equal("Characters/Freya", ((JsonElement)call.Arguments!["path"]!).GetString());
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);

        var body = GoogleAIChatClient.BuildRequestBody(
        [
            new ChatMessage(ChatRole.User, "go"),
            new ChatMessage(ChatRole.Assistant, [call]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, """{"pages":[]}""")]),
        ], null);

        var contents = body["contents"]!.AsArray();
        Assert.Equal("model", contents[1]!["role"]!.GetValue<string>());
        Assert.Equal("sig123", contents[1]!["parts"]![0]!["thoughtSignature"]!.GetValue<string>());
        var fr = contents[2]!["parts"]![0]!["functionResponse"]!;
        Assert.Equal("kb_read_page", fr["name"]!.GetValue<string>());
        Assert.NotNull(fr["response"]!["pages"]);
    }

    [Fact]
    public async Task Errors_do_not_echo_the_key()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, Url).Respond(System.Net.HttpStatusCode.BadRequest, "application/json", """{"error":{"message":"API key not valid"}}""");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new GoogleAIChatClient(mock.ToHttpClient(), Key, "gemini-test").GetResponseAsync([new ChatMessage(ChatRole.User, "x")]));
        Assert.DoesNotContain(Key, ex.Message);
    }

    [Fact]
    public async Task Lists_generate_content_models_with_the_key_in_a_header()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://generativelanguage.googleapis.com/v1beta/models*").WithHeaders("x-goog-api-key", Key)
            .Respond("application/json", """{"models":[{"name":"models/gemini-2.5-pro","supportedGenerationMethods":["generateContent"]},{"name":"models/embedding-001","supportedGenerationMethods":["embedContent"]}]}""");
        var result = await ModelCatalog.ListAsync(new LlmOptions(LlmProvider.GoogleAI, "", null, Key), mock.ToHttpClient(), false, default);
        Assert.Equal(["gemini-2.5-pro"], result.Models);
    }

    [Fact]
    public void Factory_creates_the_google_client()
    {
        var client = ChatClientFactory.Create(new LlmOptions(LlmProvider.GoogleAI, "gemini-test", null, Key), new HttpClient(), isBrowser: true);
        Assert.IsType<GoogleAIChatClient>(client);
        Assert.Equal("generativelanguage.googleapis.com", LlmDefaults.HostFor(LlmProvider.GoogleAI, null));
        Assert.True(LlmDefaults.RequiresKey(LlmProvider.GoogleAI));
        Assert.Equal("", LlmDefaults.DefaultModel(LlmProvider.GoogleAI));
    }
}
