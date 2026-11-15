using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Settings;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>Plan §10 ModelCapabilityProbe, with a scripted client.</summary>
public class ModelCapabilityProbeTests
{
    readonly ScriptedChatClient _client = new();
    readonly List<LlmOptions> _created = [];
    static readonly LlmOptions Options = new(LlmProvider.OpenAI, "gpt-test", null, "sk-unsaved");

    ModelCapabilityProbe Probe() => new(o => { _created.Add(o); return _client; });

    ScriptedChatClient ConnectOk() => _client.EnqueueText("OK");

    FunctionCallContent ReadCall(string path = "Characters/Freya") =>
        ScriptedChatClient.Call("call_1", "kb_read_page", ("path", path));

    [Fact]
    public async Task Passes_and_probes_the_options_passed_in()
    {
        ConnectOk().EnqueueCalls(ReadCall()).EnqueueText("""{"linksToOdin":["Characters/Freya.md","Characters/Balt.md"]}""");
        var result = await Probe().ProbeAsync(Options, default);
        Assert.True(result.Passed, result.Message);
        Assert.Equal([Options], _created);
    }

    [Fact]
    public async Task Stage_a_forces_a_tool_call_and_stage_b_keeps_the_tool_and_history()
    {
        ConnectOk().EnqueueCalls(ReadCall()).EnqueueText("""{"linksToOdin":["characters/balt","CHARACTERS/FREYA.md"]}""");
        Assert.True((await Probe().ProbeAsync(Options, default)).Passed);

        var a = _client.Options[1]!;
        Assert.Equal(ChatToolMode.RequireAny, a.ToolMode);
        Assert.Equal("kb_read_page", Assert.Single(a.Tools!).Name);
        Assert.Equal(ProbeConstants.MaxOutputTokens, a.MaxOutputTokens);

        var b = _client.Options[2]!;
        Assert.Equal(ChatToolMode.Auto, b.ToolMode);
        Assert.Equal("kb_read_page", Assert.Single(b.Tools!).Name);
        var history = _client.Requests[2];
        Assert.Contains(history, m => m.Role == ChatRole.Assistant && m.Contents.OfType<FunctionCallContent>().Any(c => c.CallId == "call_1"));
        var result = history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Equal("call_1", result.CallId);
        Assert.Equal(ProbeFixtures.GraphResult, result.Result);
        Assert.Equal(ProbeFixtures.GraphParseMessage, history[^1].Text);
    }

    [Fact]
    public async Task Text_instead_of_a_call_fails_at_tool_call()
    {
        ConnectOk().EnqueueText("I would read Characters/Freya.");
        var result = await Probe().ProbeAsync(Options, default);
        Assert.Equal(ProbeStage.ToolCall, result.FailedAt);
        Assert.Equal("The model answered in text and did not call kb_read_page.", result.Message);
    }

    [Fact]
    public async Task A_wrong_path_fails_at_tool_call()
    {
        ConnectOk().EnqueueCalls(ReadCall("Characters/Balt"));
        var result = await Probe().ProbeAsync(Options, default);
        Assert.Equal(ProbeStage.ToolCall, result.FailedAt);
        Assert.Contains("Characters/Balt", result.Message);
    }

    [Fact]
    public async Task Bad_json_is_repaired_and_passes()
    {
        ConnectOk().EnqueueCalls(ReadCall("characters/freya.md")).EnqueueText("Here you go:\n```json\n{\"linksToOdin\":[\"Characters/Freya.md\",\"Characters/Balt.md\",]}\n```");
        Assert.True((await Probe().ProbeAsync(Options, default)).Passed);
    }

    [Fact]
    public async Task The_wrong_set_fails_at_graph_parse()
    {
        ConnectOk().EnqueueCalls(ReadCall()).EnqueueText("""{"linksToOdin":["Characters/Freya.md","Characters/Balt.md","Places/Asgard.md"]}""");
        Assert.Equal(ProbeStage.GraphParse, (await Probe().ProbeAsync(Options, default)).FailedAt);
    }

    [Fact]
    public async Task A_second_tool_call_in_stage_b_fails()
    {
        ConnectOk().EnqueueCalls(ReadCall()).EnqueueCalls(ScriptedChatClient.Call("call_2", "kb_read_page", ("path", "Characters/Balt")));
        var result = await Probe().ProbeAsync(Options, default);
        Assert.Equal(ProbeStage.GraphParse, result.FailedAt);
        Assert.Equal("The model called a tool again instead of answering.", result.Message);
    }

    [Fact]
    public async Task Connect_failure_is_reported_at_connect()
    {
        _client.EnqueueFailure(new HttpRequestException("nope", null, System.Net.HttpStatusCode.Unauthorized));
        var result = await Probe().ProbeAsync(Options, default);
        Assert.Equal(ProbeStage.Connect, result.FailedAt);
        Assert.Contains("401", result.Message);
    }

    [Fact]
    public async Task A_rejected_require_any_is_retried_with_auto()
    {
        ConnectOk()
            .EnqueueFailure(new InvalidOperationException("tool_choice 'required' is not supported"))
            .EnqueueCalls(ReadCall())
            .EnqueueText("""{"linksToOdin":["Characters/Freya.md","Characters/Balt.md"]}""");
        Assert.True((await Probe().ProbeAsync(Options, default)).Passed);
        Assert.Equal(ChatToolMode.Auto, _client.Options[2]!.ToolMode);
        Assert.Equal(ProbeFixtures.ToolCallMessageAuto, _client.Requests[2][^1].Text);
    }

    [Fact]
    public async Task A_stage_timeout_fails_with_a_message()
    {
        var slow = new SlowClient();
        var result = await new ModelCapabilityProbe(_ => slow) { StageTimeout = TimeSpan.FromMilliseconds(50) }.ProbeAsync(Options, default);
        Assert.Equal(ProbeStage.Connect, result.FailedAt);
        Assert.Equal("The request timed out.", result.Message);
    }

    [Fact]
    public async Task Missing_key_fails_before_any_request()
    {
        var result = await new ModelCapabilityProbe(o => ChatClientFactory.Create(o, new HttpClient(), false))
            .ProbeAsync(Options with { ApiKey = null }, default);
        Assert.Equal(ProbeStage.Connect, result.FailedAt);
        Assert.Contains("API key", result.Message);
    }

    sealed class SlowClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
