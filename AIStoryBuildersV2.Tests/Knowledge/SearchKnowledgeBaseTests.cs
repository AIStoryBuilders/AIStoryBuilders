using System.Text.Json;
using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.KnowledgeAssist;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Services;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.StoryChat;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>Plan §10: kb_ tools, KnowledgeContextRetriever, and "Search knowledgebase" in chat and paragraph writing (D14-D16).</summary>
public class SearchKnowledgeBaseTests : IDisposable
{
    const string Title = FixtureFactory.Title;
    readonly ServiceFixture _f;
    readonly ScriptedChatClient _chat = new();
    readonly StoryKnowledgeBaseProvider _kbs;

    public SearchKnowledgeBaseTests()
    {
        _f = new ServiceFixture(chat: _chat);
        _kbs = new StoryKnowledgeBaseProvider(new TestHost(_f.Dir.Path), new PermanentFileDeleter(), _f.Persistence);
    }

    public void Dispose() => _f.Dispose();

    async Task<IStoryKnowledgeBase> SeedAsync()
    {
        var kb = await _kbs.OpenAsync(Title);
        await kb.Workspace.SavePageAsync("Lore/Necklace.md", "---\ntitle: The Necklace\n---\nThe Necklace was forged by Hrafnkel from nine silver beads.\n", 0);
        await kb.Workspace.SavePageAsync("Hrafnkel.md", "# Hrafnkel\nA smith in the fjord village.\n", 0);
        return kb;
    }

    KnowledgeContextRetriever Retriever(IChatClientProvider? clients = null) =>
        new(_kbs, clients ?? new FakeChatClientProvider(_chat), _f.Log);

    static string Arg(FunctionCallContent call, string name) => call.Arguments![name]!.ToString()!;

    // ── KnowledgeBaseTools ──────────────────────────────────────────

    [Fact]
    public async Task Tools_are_read_only_and_prefixed()
    {
        var tools = new KnowledgeBaseTools(await SeedAsync()).CreateReadOnly();

        Assert.Equal(["kb_get_links", "kb_list_folder", "kb_read_page", "kb_search_pages"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Read_page_truncates_long_pages()
    {
        var kb = await SeedAsync();
        await kb.Workspace.SavePageAsync("Long.md", new string('x', 500), 0);

        var json = JsonDocument.Parse(await new KnowledgeBaseTools(kb, maxToolResultChars: 100).ReadPageAsync("Long.md")).RootElement;

        Assert.True(json.GetProperty("truncated").GetBoolean());
        Assert.Equal(100, json.GetProperty("markdown").GetString()!.Length);
    }

    [Theory]
    [InlineData("../AIStoryBuildersStories.csv")]
    [InlineData("../../secret.md")]
    [InlineData("C:/Windows/win.ini")]
    public async Task Paths_stay_inside_the_story_knowledge_base(string path)
    {
        var tools = new KnowledgeBaseTools(await SeedAsync());

        Assert.Contains("\"error\"", await tools.ReadPageAsync(path));
        Assert.Contains("\"error\"", await tools.ListFolderAsync(path));
    }

    [Fact]
    public async Task Search_finds_pages_with_snippets()
    {
        var json = await new KnowledgeBaseTools(await SeedAsync()).SearchPagesAsync("forged");

        Assert.Contains("Lore/Necklace.md", json);
    }

    // ── KnowledgeContextRetriever ───────────────────────────────────

    [Fact]
    public async Task Retriever_returns_cited_notes()
    {
        await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_search_pages", ("query", "necklace")))
             .EnqueueCalls(ScriptedChatClient.Call("c2", "kb_read_page", ("path", "Lore/Necklace.md")))
             .EnqueueText("- The Necklace has nine silver beads. [[Lore/Necklace]]");

        var ctx = await Retriever().RetrieveAsync(Title, "Freya's necklace", "test-model", CancellationToken.None);

        Assert.Null(ctx.Warning);
        Assert.Contains("nine silver beads", ctx.Notes);
        Assert.Equal(["Lore/Necklace"], ctx.CitedPages);
        var read = _chat.Requests[2].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        Assert.Contains("forged by Hrafnkel", read.Result?.ToString());
    }

    [Fact]
    public async Task No_relevant_pages_gives_empty_notes()
    {
        await SeedAsync();
        _chat.EnqueueText(KnowledgeAssistConstants.NoRelevantPages);

        var ctx = await Retriever().RetrieveAsync(Title, "dragons", "test-model", CancellationToken.None);

        Assert.Equal("", ctx.Notes);
        Assert.Null(ctx.Warning);
    }

    [Fact]
    public async Task A_provider_error_gives_a_warning_and_no_exception()
    {
        await SeedAsync();
        _chat.EnqueueFailure(new HttpRequestException("503 Service Unavailable"));

        var ctx = await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.Equal("", ctx.Notes);
        Assert.Contains("503", ctx.Warning);
    }

    [Fact]
    public async Task An_unverified_model_gives_a_warning()
    {
        await SeedAsync();
        var ctx = await Retriever(new FakeChatClientProvider(null, "Model 'x' has not passed the capability check. Verify it in Settings."))
            .RetrieveAsync(Title, "necklace", "x", CancellationToken.None);

        Assert.Contains("capability check", ctx.Warning);
        Assert.Empty(_chat.Requests);
    }

    [Fact]
    public async Task The_loop_stops_after_four_model_calls_and_the_last_has_no_tools()
    {
        await SeedAsync();
        for (var i = 0; i < 6; i++) _chat.EnqueueCalls(ScriptedChatClient.Call("c" + i, "kb_search_pages", ("query", "x")));

        var ctx = await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.Equal(KnowledgeAssistConstants.MaxIterations, _chat.Requests.Count);
        Assert.NotNull(_chat.Options[0]?.Tools);
        Assert.Null(_chat.Options[^1]?.Tools);
        Assert.Equal("", ctx.Notes);
        Assert.NotNull(ctx.Warning);
    }

    [Fact]
    public async Task Notes_are_capped_at_4000_characters()
    {
        await SeedAsync();
        _chat.EnqueueText("[[Lore/Necklace]] " + new string('n', 5000));

        var ctx = await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.Equal(KnowledgeAssistConstants.MaxKnowledgeNotesChars, ctx.Notes.Length);
        Assert.True(ctx.Truncated);
    }

    [Fact]
    public async Task An_empty_knowledge_base_needs_no_model_call()
    {
        var ctx = await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.Equal(KnowledgeContext.Empty, ctx);
        Assert.Empty(_chat.Requests);
    }

    // ── Chat and paragraph surfaces ─────────────────────────────────

    StoryChatService Chat() => new(new GraphQueryService(_f.GraphCache), new GraphMutationService(_f.Service, new GraphBuilder(), _f.GraphCache),
        new FakeChatClientProvider(_chat), new StubSettings(), _f.Service, _f.GraphCache, _f.Log, _kbs);

    static async Task SendAsync(IStoryChatService chat, bool kb)
    {
        await foreach (var _ in chat.SendMessageAsync(new StoryChatRequest(Title, "What is the Necklace?", "test-model", kb))) { }
    }

    [Fact]
    public async Task Chat_offers_kb_tools_and_the_addendum_only_when_checked()
    {
        await SeedAsync();
        _chat.EnqueueText("unchecked").EnqueueText("checked");
        var chat = Chat();

        await SendAsync(chat, kb: false);
        await SendAsync(chat, kb: true);

        // Q21: unchecked offers only the two deletes, and no read tool or other edit.
        Assert.Equal(["kb_delete_folder", "kb_delete_page"],
            _chat.Options[0]!.Tools!.Select(t => t.Name).Where(n => n.StartsWith("kb_", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Use kb_search_pages", _chat.Requests[0][0].Text);
        Assert.Contains("Include Knowledgebase is off", _chat.Requests[0][0].Text);
        Assert.Contains(_chat.Options[1]!.Tools!, t => t.Name == "kb_read_page");
        Assert.Contains("The story has a knowledge base.", _chat.Requests[1][0].Text);
        Assert.DoesNotContain("Include Knowledgebase is off", _chat.Requests[1][0].Text);
    }

    [Fact]
    public async Task Unchecked_chat_can_still_propose_deleting_a_page_and_a_folder()
    {
        var kb = await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_delete_page", ("path", "Hrafnkel.md")),
                           ScriptedChatClient.Call("c2", "kb_delete_folder", ("path", "Lore")))
             .EnqueueText("Proposed.");
        var chat = Chat();

        await SendAsync(chat, kb: false);

        var pending = chat.PendingChanges(Title);
        Assert.Equal(["kb_delete_page", "kb_delete_folder"], pending.Select(p => p.ToolName));
        Assert.True(await kb.Workspace.ExistsAsync("Hrafnkel.md"));
        foreach (var p in pending) Assert.True((await chat.ApplyPendingAsync(Title, p.Id))!.Success);
        Assert.False(await kb.Workspace.ExistsAsync("Hrafnkel.md"));
        Assert.False(await kb.Workspace.ExistsAsync("Lore"));
    }

    [Fact]
    public async Task Unchecked_chat_cannot_read_or_change_pages_other_than_deleting()
    {
        await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_read_page", ("path", "Lore/Necklace.md")),
                           ScriptedChatClient.Call("c2", "kb_update_page", ("path", "Hrafnkel.md"), ("markdown", "# x\n")))
             .EnqueueText("I can't.");
        var chat = Chat();

        await SendAsync(chat, kb: false);

        Assert.Empty(chat.PendingChanges(Title));
        var results = _chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString()).ToList();
        Assert.All(results, r => Assert.Contains("Unknown tool", r));
        Assert.DoesNotContain(results, r => r!.Contains("nine silver beads"));
    }

    [Fact]
    public async Task Unchecked_chat_with_an_empty_knowledge_base_offers_no_kb_tools()
    {
        _chat.EnqueueText("ok");

        await SendAsync(Chat(), kb: false);

        Assert.DoesNotContain(_chat.Options[0]!.Tools!, t => t.Name.StartsWith("kb_", StringComparison.Ordinal));
        Assert.DoesNotContain("knowledge base", _chat.Requests[0][0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_reads_the_story_knowledge_base_through_kb_tools()
    {
        await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_read_page", ("path", "Lore/Necklace.md")))
             .EnqueueText("Hrafnkel forged it. [[Lore/Necklace]]");

        await SendAsync(Chat(), kb: true);

        var result = _chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains("nine silver beads", result.Result?.ToString());
    }

    [Fact]
    public async Task Chat_with_an_empty_knowledge_base_offers_no_kb_tools()
    {
        _chat.EnqueueText("ok");

        await SendAsync(Chat(), kb: true);

        Assert.DoesNotContain(_chat.Options[0]!.Tools!, t => t.Name.StartsWith("kb_", StringComparison.Ordinal));
    }

    // ── Chat edits to knowledge base pages ──────────────────────────

    static async Task SendAsync(IStoryChatService chat, string message)
    {
        await foreach (var _ in chat.SendMessageAsync(new StoryChatRequest(Title, message, "test-model", true))) { }
    }

    async Task<string> PageAsync(string path) => (await (await _kbs.OpenAsync(Title)).Workspace.ReadPageAsync(path)).Markdown;

    [Fact]
    public async Task Chat_offers_the_page_edit_tools_with_the_read_tools_and_mentions_them()
    {
        await SeedAsync();
        _chat.EnqueueText("ok");

        await SendAsync(Chat(), kb: true);

        var names = _chat.Options[0]!.Tools!.Select(t => t.Name).ToList();
        Assert.Contains("kb_create_page", names);
        Assert.Contains("kb_update_page", names);
        Assert.Contains("kb_delete_page", names);
        Assert.Contains("kb_update_page", _chat.Requests[0][0].Text);
    }

    [Fact]
    public async Task The_retriever_for_paragraph_writing_stays_read_only()
    {
        await SeedAsync();
        _chat.EnqueueText(KnowledgeAssistConstants.NoRelevantPages);

        await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.DoesNotContain(_chat.Options[0]!.Tools!, t => KnowledgeBaseTools.EditToolNames.Contains(t.Name) || KnowledgeBaseTools.FolderToolNames.Contains(t.Name));
    }

    // ── Chat folder management ──────────────────────────────────────

    async Task<IStoryChatService> ProposeAsync(params FunctionCallContent[] calls)
    {
        _chat.EnqueueCalls(calls).EnqueueText("Proposed.");
        var chat = Chat();
        await SendAsync(chat, "Tidy the folders.");
        return chat;
    }

    async Task ApplyAllAsync(IStoryChatService chat)
    {
        foreach (var p in chat.PendingChanges(Title))
        {
            var result = await chat.ApplyPendingAsync(Title, p.Id);
            Assert.True(result!.Success, result.Error);
        }
    }

    [Fact]
    public async Task Chat_offers_the_folder_tools()
    {
        await SeedAsync();
        _chat.EnqueueText("ok");

        await SendAsync(Chat(), kb: true);

        var names = _chat.Options[0]!.Tools!.Select(t => t.Name).ToHashSet();
        Assert.Superset(KnowledgeBaseTools.FolderToolNames.ToHashSet(), names);
        Assert.Contains("kb_move_folder", _chat.Requests[0][0].Text);
    }

    [Fact]
    public async Task Chat_creates_a_nested_folder_only_on_Apply()
    {
        await SeedAsync();
        var chat = await ProposeAsync(ScriptedChatClient.Call("c1", "kb_create_folder", ("path", "Places/North")));
        var kb = await _kbs.OpenAsync(Title);

        Assert.Equal("kb_create_folder", Assert.Single(chat.PendingChanges(Title)).ToolName);
        Assert.False(await kb.Workspace.ExistsAsync("Places/North"));

        await ApplyAllAsync(chat);

        Assert.True(await kb.Workspace.ExistsAsync("Places/North"));
    }

    [Fact]
    public async Task Chat_renames_a_folder_and_links_to_its_pages_follow()
    {
        var kb = await SeedAsync();
        await kb.Workspace.SavePageAsync("Freya.md", "Freya wears [[Lore/Necklace]].\n", 0);
        var chat = await ProposeAsync(ScriptedChatClient.Call("c1", "kb_rename_folder", ("path", "Lore"), ("newName", "Artifacts")));

        var proposal = Assert.Single(chat.PendingChanges(Title));
        Assert.Contains("Lore to Artifacts", proposal.Preview.Summary);
        var result = await chat.ApplyPendingAsync(Title, proposal.Id);
        Assert.Equal(proposal.Preview.Summary + ". Links updated in 1 pages",
            AIStoryBuildersV2.Shared.Components.Chat.StoryChatTab.AppliedSummary(proposal, result!));

        Assert.True(await kb.Workspace.ExistsAsync("Artifacts/Necklace.md"));
        Assert.False(await kb.Workspace.ExistsAsync("Lore"));
        Assert.Contains("[[Artifacts/Necklace]]", await PageAsync("Freya.md"));
    }

    [Fact]
    public async Task Chat_moves_a_folder_into_another_and_back_to_the_root()
    {
        var kb = await SeedAsync();
        await kb.Workspace.CreateFolderAsync("World");
        var chat = await ProposeAsync(ScriptedChatClient.Call("c1", "kb_move_folder", ("path", "Lore"), ("newParentFolder", "World")));
        await ApplyAllAsync(chat);
        Assert.True(await kb.Workspace.ExistsAsync("World/Lore/Necklace.md"));

        _chat.EnqueueCalls(ScriptedChatClient.Call("c2", "kb_move_folder", ("path", "World/Lore"), ("newParentFolder", ""))).EnqueueText("Proposed.");
        await SendAsync(chat, "Move it back.");
        await ApplyAllAsync(chat);

        Assert.True(await kb.Workspace.ExistsAsync("Lore/Necklace.md"));
    }

    [Fact]
    public async Task Chat_deletes_a_folder_with_its_pages_and_says_how_many()
    {
        var kb = await SeedAsync();
        var chat = await ProposeAsync(ScriptedChatClient.Call("c1", "kb_delete_folder", ("path", "Lore")));

        Assert.Contains("with its 1 page and 0 subfolders", Assert.Single(chat.PendingChanges(Title)).Preview.Summary);
        Assert.True(await kb.Workspace.ExistsAsync("Lore/Necklace.md"));
        await ApplyAllAsync(chat);

        Assert.False(await kb.Workspace.ExistsAsync("Lore"));
        Assert.True(await kb.Workspace.ExistsAsync("Hrafnkel.md"));
    }

    [Fact]
    public async Task A_folder_delete_is_refused_if_pages_were_added_after_the_proposal()
    {
        var kb = await SeedAsync();
        var chat = await ProposeAsync(ScriptedChatClient.Call("c1", "kb_delete_folder", ("path", "Lore")));
        await kb.Workspace.SavePageAsync("Lore/Runes.md", "# Runes\n", 0);

        var result = await chat.ApplyPendingAsync(Title, Assert.Single(chat.PendingChanges(Title)).Id);

        Assert.False(result!.Success);
        Assert.Contains("changed since", result.Error);
        Assert.True(await kb.Workspace.ExistsAsync("Lore/Runes.md"));
    }

    [Theory]
    [InlineData("kb_create_folder", "Lore", "", "already exists")]
    [InlineData("kb_rename_folder", "Missing", "New", "No folder at")]
    [InlineData("kb_rename_folder", "Lore", "a/b", "contain")]
    [InlineData("kb_move_folder", "Lore", "Lore", "into itself")]
    [InlineData("kb_move_folder", "Lore", "Nowhere", "No folder at")]
    [InlineData("kb_delete_folder", "", "", "A folder path is required")]
    [InlineData("kb_delete_folder", "Hrafnkel.md", "", "is a page")]
    [InlineData("kb_delete_folder", "../..", "", "are not allowed")]
    public async Task Invalid_folder_changes_are_refused_without_a_proposal(string tool, string path, string argument, string error)
    {
        await SeedAsync();
        var chat = await ProposeAsync(ScriptedChatClient.Call("c1", tool, ("path", path), ("newName", argument), ("newParentFolder", argument)));

        Assert.Empty(chat.PendingChanges(Title));
        var result = _chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains(error, result.Result?.ToString());
    }

    [Fact]
    public async Task Chat_proposes_a_page_rewrite_and_writes_it_only_on_Apply()
    {
        await SeedAsync();
        const string updated = "# Hrafnkel\nA smith in the fjord village. He forged the Necklace.\n";
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_update_page", ("path", "Hrafnkel"), ("markdown", updated)))
             .EnqueueText("I proposed the change.");
        var chat = Chat();

        await SendAsync(chat, "Note on Hrafnkel's page that he forged the Necklace.");

        var pending = Assert.Single(chat.PendingChanges(Title));
        Assert.Equal("kb_update_page", pending.ToolName);
        Assert.Contains(pending.Arguments, a => a.Key == "markdown" && a.Value == updated);
        Assert.Equal(["KnowledgeBase/Hrafnkel.md"], pending.Preview.AffectedFiles);
        Assert.DoesNotContain("forged", await PageAsync("Hrafnkel.md"));

        var result = await chat.ApplyPendingAsync(Title, pending.Id);

        Assert.True(result!.Success, result.Error);
        Assert.Equal(updated, await PageAsync("Hrafnkel.md"));
        Assert.Empty(chat.PendingChanges(Title));
    }

    [Fact]
    public async Task Chat_can_create_and_delete_pages_in_any_folder()
    {
        await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_create_page", ("path", "Places/Fjord/Village.md"), ("markdown", "# Village\nHome of Hrafnkel.\n")),
                           ScriptedChatClient.Call("c2", "kb_delete_page", ("path", "Lore/Necklace.md")))
             .EnqueueText("Proposed.");
        var chat = Chat();

        await SendAsync(chat, "Add a village page and remove the necklace page.");

        var pending = chat.PendingChanges(Title);
        Assert.Equal(["kb_create_page", "kb_delete_page"], pending.Select(p => p.ToolName));
        foreach (var p in pending) Assert.True((await chat.ApplyPendingAsync(Title, p.Id))!.Success);

        var kb = await _kbs.OpenAsync(Title);
        Assert.Equal("# Village\nHome of Hrafnkel.\n", await PageAsync("Places/Fjord/Village.md"));
        Assert.False(await kb.Workspace.ExistsAsync("Lore/Necklace.md"));
    }

    [Fact]
    public async Task Discarding_a_page_edit_writes_nothing()
    {
        await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_delete_page", ("path", "Hrafnkel.md"))).EnqueueText("Proposed.");
        var chat = Chat();

        await SendAsync(chat, "Delete Hrafnkel.");

        Assert.True(chat.DiscardPending(Title, Assert.Single(chat.PendingChanges(Title)).Id));
        Assert.True(await (await _kbs.OpenAsync(Title)).Workspace.ExistsAsync("Hrafnkel.md"));
    }

    [Fact]
    public async Task Apply_does_not_overwrite_a_page_the_user_saved_after_the_proposal()
    {
        var kb = await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_update_page", ("path", "Hrafnkel.md"), ("markdown", "# Hrafnkel\nAI text.\n")))
             .EnqueueText("Proposed.");
        var chat = Chat();
        await SendAsync(chat, "Rewrite Hrafnkel.");
        var pending = Assert.Single(chat.PendingChanges(Title));

        var current = await kb.Workspace.ReadPageAsync("Hrafnkel.md");
        await kb.Workspace.SavePageAsync("Hrafnkel.md", "# Hrafnkel\nThe user's text.\n", current.ModifiedTicks);
        var result = await chat.ApplyPendingAsync(Title, pending.Id);

        Assert.False(result!.Success);
        Assert.Contains("changed since", result.Error);
        Assert.Equal("# Hrafnkel\nThe user's text.\n", await PageAsync("Hrafnkel.md"));
    }

    [Theory]
    [InlineData("kb_create_page", "Hrafnkel.md", "already exists")]
    [InlineData("kb_update_page", "Missing.md", "No page at")]
    [InlineData("kb_delete_page", "Missing.md", "No page at")]
    [InlineData("kb_create_page", "../../escape.md", "are not allowed")]
    [InlineData("kb_update_page", "C:/Windows/win.ini", "Drive letters are not allowed")]
    public async Task Invalid_page_edits_are_refused_without_a_proposal(string tool, string path, string error)
    {
        await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", tool, ("path", path), ("markdown", "# New text\n"))).EnqueueText("Sorry.");
        var chat = Chat();

        await SendAsync(chat, "Edit a page.");

        Assert.Empty(chat.PendingChanges(Title));
        var result = _chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains(error, result.Result?.ToString());
    }

    [Theory]
    [InlineData("append to page Hrafnkel.md: He forged the Necklace.", "kb_update_page")]
    [InlineData("create a Village page", "kb_create_page")]
    [InlineData("delete page Hrafnkel.md", "kb_delete_page")]
    [InlineData("create folder Places/North", "kb_create_folder")]
    [InlineData("rename folder Lore to Artifacts", "kb_rename_folder")]
    [InlineData("move folder Lore to root", "kb_create_folder")]   // Lore is already at the root: refused, so no card
    [InlineData("delete folder Lore", "kb_delete_folder")]
    public async Task The_debug_fake_provider_drives_each_kb_proposal(string message, string tool)
    {
        await SeedAsync();
        var chat = new StoryChatService(new GraphQueryService(_f.GraphCache), new GraphMutationService(_f.Service, new GraphBuilder(), _f.GraphCache),
            new FakeChatClientProvider(new AIStoryBuildersV2.Shared.Llm.FakeChatClient()), new StubSettings(), _f.Service, _f.GraphCache, _f.Log, _kbs);

        var answer = "";
        await foreach (var chunk in chat.SendMessageAsync(new StoryChatRequest(Title, message, "fake", true))) answer += chunk;

        var pending = chat.PendingChanges(Title);
        if (message.StartsWith("move", StringComparison.Ordinal))
        {
            Assert.Empty(pending);
            Assert.Contains("already in that folder", answer);
            return;
        }
        Assert.Equal(tool, Assert.Single(pending).ToolName);
        Assert.Contains("Click Apply", answer);
        Assert.True((await chat.ApplyPendingAsync(Title, pending[0].Id))!.Success);
        if (tool == "kb_update_page") Assert.EndsWith("He forged the Necklace.\n", await PageAsync("Hrafnkel.md"));
    }

    [Fact]
    public void Chat_is_the_last_workspace_tab_after_Knowledgebase()
    {
        var labels = AIStoryBuildersV2.Shared.Pages.StoryWorkspace.Tabs.Select(t => t.Label).ToArray();

        Assert.Equal(["Details", "Timelines", "Locations", "Characters", "Chapters", "Knowledgebase", "Chat"], labels);
    }

    [Fact]
    public async Task Checkbox_defaults_follow_the_knowledge_base_and_are_remembered_per_surface()
    {
        var choice = new KnowledgeSearchChoice(new AppSession(), _kbs);
        Assert.Equal((true, false), await choice.DefaultAsync(Title, KnowledgeSearchChoice.Chat));

        await SeedAsync();
        Assert.Equal((false, true), await choice.DefaultAsync(Title, KnowledgeSearchChoice.Chat));

        choice.Remember(Title, KnowledgeSearchChoice.Chat, false);
        Assert.Equal((false, false), await choice.DefaultAsync(Title, KnowledgeSearchChoice.Chat));
        Assert.Equal((false, true), await choice.DefaultAsync(Title, KnowledgeSearchChoice.Paragraph));
    }

    ParagraphAiHelper Helper() =>
        new(_f.Service, _f.Orchestrator, new KnowledgeSearchChoice(new AppSession(), _kbs), Retriever());

    async Task<(Story Story, Chapter Chapter, Paragraph Paragraph, List<Character> Characters)> FixtureParagraphAsync()
    {
        var story = (await _f.Service.GetStorysAsync()).Single();
        var chapter = _f.Service.GetChapters(story).First();
        var paragraph = _f.Service.GetParagraphs(chapter).First();
        return (story, chapter, paragraph, _f.Service.GetCharacters(story));
    }

    [Fact]
    public async Task A_checked_generate_puts_knowledge_base_notes_in_the_prompt()
    {
        await SeedAsync();
        var (story, chapter, paragraph, characters) = await FixtureParagraphAsync();
        _chat.EnqueueText("- The Necklace has nine silver beads. [[Lore/Necklace]]")
             .EnqueueText("{\"paragraph_content\":\"Freya counted nine beads.\"}");
        var prompt = new AIPrompt { AIPromptText = "Freya looks at her necklace", NumberOfWords = "50", ModelId = "test-model", SearchKnowledgeBase = true };
        var warnings = new List<string>();

        var text = await Helper().GenerateAsync(story, chapter, paragraph, characters, prompt, warnings.Add);

        Assert.Equal("Freya counted nine beads.", text);
        Assert.Empty(warnings);
        Assert.Contains("Freya looks at her necklace", _chat.Requests[0][^1].Text);
        var write = _chat.Requests[^1][^1].Text;
        Assert.Contains("<knowledge_base_notes>", write);
        Assert.Contains("nine silver beads", write);
    }

    [Fact]
    public async Task An_unchecked_generate_never_touches_the_knowledge_base()
    {
        await SeedAsync();
        var (story, chapter, paragraph, characters) = await FixtureParagraphAsync();
        _chat.EnqueueText("{\"paragraph_content\":\"Freya waited.\"}");
        var prompt = new AIPrompt { AIPromptText = "Freya waits", NumberOfWords = "50", ModelId = "test-model", SearchKnowledgeBase = false };

        await Helper().GenerateAsync(story, chapter, paragraph, characters, prompt, _ => { });

        Assert.Single(_chat.Requests);
        Assert.DoesNotContain("knowledge_base_notes", _chat.Requests[0][^1].Text);
        Assert.Null(_chat.Options[0]?.Tools);
    }

    [Fact]
    public async Task A_failed_search_warns_and_still_writes_the_paragraph()
    {
        await SeedAsync();
        var (story, chapter, paragraph, characters) = await FixtureParagraphAsync();
        _chat.EnqueueFailure(new HttpRequestException("timeout"))
             .EnqueueText("{\"paragraph_content\":\"Freya waited.\"}");
        var prompt = new AIPrompt { AIPromptText = "Freya waits", NumberOfWords = "50", ModelId = "test-model", SearchKnowledgeBase = true };
        var warnings = new List<string>();

        var text = await Helper().GenerateAsync(story, chapter, paragraph, characters, prompt, warnings.Add);

        Assert.Equal("Freya waited.", text);
        Assert.Single(warnings);
        Assert.DoesNotContain("knowledge_base_notes", _chat.Requests[^1][^1].Text);
    }
}
