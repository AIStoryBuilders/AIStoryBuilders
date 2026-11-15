using System.ComponentModel;
using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.StoryChat;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>Plan §11: Story Chat on the shared client, graph tools per turn, kb_ tools only on request.</summary>
public class StoryChatServiceTests : IDisposable
{
    readonly ServiceFixture _f = new();
    readonly ScriptedChatClient _chat = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Supplies one kb_ tool so the tests can see when the service offers it.</summary>
    sealed class KbStubChatService(ServiceFixture f, IChatClientProvider clients)
        : StoryChatService(new GraphQueryService(f.GraphCache), new GraphMutationService(f.Service, new GraphBuilder(), f.GraphCache),
            clients, new StubSettings(), f.Service, f.GraphCache, f.Log)
    {
        protected override Task<IList<AITool>> KnowledgeBaseToolsAsync(string storyTitle, CancellationToken ct) =>
            Task.FromResult<IList<AITool>>([AIFunctionFactory.Create([Description("stub")] () => "[]", "kb_search_pages")]);
    }

    StoryChatService Create(IChatClientProvider? clients = null) => new KbStubChatService(_f, clients ?? new FakeChatClientProvider(_chat));

    static async Task<string> SendAsync(IStoryChatService chat, StoryChatRequest request)
    {
        var text = "";
        await foreach (var chunk in chat.SendMessageAsync(request)) text += chunk;
        return text;
    }

    static StoryChatRequest Ask(string message, bool kb = false) => new(FixtureFactory.Title, message, "test-model", kb);

    static IEnumerable<string> ToolNames(ChatOptions? options) => options?.Tools?.Select(t => t.Name) ?? [];

    [Fact]
    public async Task Graph_tools_are_always_offered_and_kb_tools_only_when_requested()
    {
        _chat.EnqueueText("one").EnqueueText("two");
        var chat = Create();

        await SendAsync(chat, Ask("Who is Balt?"));
        await SendAsync(chat, Ask("Who is Balt?", kb: true));

        Assert.Contains("GetCharacters", ToolNames(_chat.Options[0]));
        Assert.DoesNotContain(ToolNames(_chat.Options[0]), n => n.StartsWith("kb_", StringComparison.Ordinal));
        Assert.Contains("GetCharacters", ToolNames(_chat.Options[1]));
        Assert.Contains("kb_search_pages", ToolNames(_chat.Options[1]));
    }

    [Fact]
    public async Task An_unverified_model_returns_the_provider_reason_without_calling_a_model()
    {
        const string reason = "Model 'x' has not passed the capability check. Verify it in Settings.";
        var chat = Create(new FakeChatClientProvider(null, reason));

        var answer = await SendAsync(chat, Ask("Hello"));

        Assert.Equal(reason, answer);
        Assert.Empty(_chat.Requests);
    }

    [Fact]
    public async Task Tool_calls_run_against_the_requested_story_graph()
    {
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "GetCharacters"))
             .EnqueueText("Balt and Freya.");
        var chat = Create();

        var answer = await SendAsync(chat, Ask("Who are the characters?"));

        Assert.Equal("Balt and Freya.", answer);
        var toolResult = _chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains("Balt", toolResult.Result?.ToString());
        Assert.Null(StoryGraphContext.Current);
    }

    [Fact]
    public async Task A_mutation_preview_writes_nothing()
    {
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "AddCharacter", ("name", "Mira"), ("type", "Supporting"),
                ("description", "A smith."), ("timelineName", null), ("confirmed", false)))
             .EnqueueText("Shall I add Mira?");
        var chat = Create();
        await _f.Service.EnsureGraphExistsAsync(FixtureFactory.Title);
        var before = Directory.GetFiles(_f.StoryDir, "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        _f.Persistence.Dirty.Clear();

        var answer = await SendAsync(chat, Ask("Add a smith called Mira"));

        Assert.Equal("Shall I add Mira?", answer);
        var after = Directory.GetFiles(_f.StoryDir, "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        Assert.Equal(before, after);
        Assert.False(File.Exists(Path.Combine(_f.StoryDir, "Characters", "Mira.csv")));
    }

    [Fact]
    public async Task History_is_kept_per_story_and_cleared_by_ClearSession()
    {
        _chat.EnqueueText("first").EnqueueText("second").EnqueueText("third");
        var chat = Create();

        await SendAsync(chat, Ask("Q1"));
        await SendAsync(chat, Ask("Q2"));
        Assert.Contains(_chat.Requests[1], m => m.Role == ChatRole.Assistant && m.Text == "first");

        chat.ClearSession(FixtureFactory.Title);
        await SendAsync(chat, Ask("Q3"));
        Assert.DoesNotContain(_chat.Requests[2], m => m.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task A_failed_turn_is_not_added_to_the_history()
    {
        _chat.EnqueueFailure(new HttpRequestException("timeout")).EnqueueText("ok");
        var chat = Create();

        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(chat, Ask("Q1")));
        await SendAsync(chat, Ask("Q1"));

        Assert.Single(_chat.Requests[1], m => m.Role == ChatRole.User);
    }

    List<string> Tree() =>
        Directory.GetFiles(_f.StoryDir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => f + "|" + File.ReadAllText(f)).ToList();

    int ChapterCount() => Directory.GetDirectories(Path.Combine(_f.StoryDir, "Chapters")).Length;

    /// <summary>The injected call from SEC-001: the model sets confirmed=true itself.</summary>
    async Task<StoryChatService> ProposeDeleteChapterAsync()
    {
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "DeleteChapter", ("chapterNumber", 1), ("confirmed", true)))
             .EnqueueText("Done.");
        var chat = Create();
        await _f.Service.EnsureGraphExistsAsync(FixtureFactory.Title);
        _f.Persistence.Dirty.Clear();
        await SendAsync(chat, Ask("Summarize chapter 1"));
        return chat;
    }

    [Fact]
    public async Task No_mutation_tool_lets_the_model_confirm_its_own_change()
    {
        _chat.EnqueueText("ok");
        var chat = Create();
        await SendAsync(chat, Ask("hi"));

        var tools = _chat.Options[0]!.Tools!.OfType<AIFunction>().ToList();
        Assert.Contains(tools, t => t.Name == "DeleteChapter");
        Assert.All(tools, t => Assert.DoesNotContain("confirmed", t.JsonSchema.GetRawText(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_model_that_passes_confirmed_true_only_creates_a_proposal()
    {
        await _f.Service.EnsureGraphExistsAsync(FixtureFactory.Title);
        var before = Tree();
        var chapters = ChapterCount();

        var chat = await ProposeDeleteChapterAsync();

        Assert.Equal(before, Tree());
        Assert.Equal(chapters, ChapterCount());
        Assert.Empty(_f.Persistence.Dirty);
        var pending = Assert.Single(chat.PendingChanges(FixtureFactory.Title));
        Assert.Equal("DeleteChapter", pending.ToolName);
        Assert.Contains(pending.Arguments, a => a.Key == "chapterNumber" && a.Value == "1");
        Assert.DoesNotContain(pending.Arguments, a => a.Key == "confirmed");
        var toolResult = _chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains(pending.Id, toolResult.Result?.ToString());
    }

    [Fact]
    public async Task Apply_runs_the_proposal_once_and_marks_the_story_dirty()
    {
        var chapters = ChapterCount();
        var chat = await ProposeDeleteChapterAsync();
        var id = chat.PendingChanges(FixtureFactory.Title).Single().Id;

        var result = await chat.ApplyPendingAsync(FixtureFactory.Title, id);

        Assert.True(result?.Success);
        Assert.Equal(chapters - 1, ChapterCount());
        Assert.Contains(FixtureFactory.Title, _f.Persistence.Dirty);
        Assert.Empty(chat.PendingChanges(FixtureFactory.Title));
        Assert.Null(await chat.ApplyPendingAsync(FixtureFactory.Title, id));
        Assert.Equal(chapters - 1, ChapterCount());
        Assert.Null(StoryGraphContext.Current);
    }

    [Fact]
    public async Task A_proposal_cannot_be_applied_through_another_story()
    {
        var chapters = ChapterCount();
        var chat = await ProposeDeleteChapterAsync();
        var id = chat.PendingChanges(FixtureFactory.Title).Single().Id;

        Assert.Null(await chat.ApplyPendingAsync("Another Story", id));
        Assert.False(chat.DiscardPending("Another Story", id));

        Assert.Equal(chapters, ChapterCount());
        Assert.Single(chat.PendingChanges(FixtureFactory.Title));
    }

    [Fact]
    public async Task Discard_drops_the_proposal_and_tells_the_model_next_turn()
    {
        var chapters = ChapterCount();
        var chat = await ProposeDeleteChapterAsync();
        var id = chat.PendingChanges(FixtureFactory.Title).Single().Id;

        Assert.True(chat.DiscardPending(FixtureFactory.Title, id));
        Assert.Empty(chat.PendingChanges(FixtureFactory.Title));
        _chat.EnqueueText("Understood.");
        await SendAsync(chat, Ask("OK"));

        Assert.Equal(chapters, ChapterCount());
        Assert.Contains("discarded the proposed change", _chat.Requests[^1].Last(m => m.Role == ChatRole.User).Text);
    }

    [Fact]
    public async Task ClearSession_discards_pending_proposals()
    {
        var chat = await ProposeDeleteChapterAsync();

        chat.ClearSession(FixtureFactory.Title);

        Assert.Empty(chat.PendingChanges(FixtureFactory.Title));
    }
}
