using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Models.JSON;
using AIStoryBuildersV2.Shared.Stories;
using Microsoft.Extensions.AI;
using Character = AIStoryBuildersV2.Shared.Models.Character;

namespace AIStoryBuildersV2.Tests;

/// <summary>T7a/T7b: the orchestrator against scripted clients (no network).</summary>
public class OrchestratorTests : IDisposable
{
    readonly CultureScope _culture = new("en-US");
    readonly ScriptedChatClient _chat = new();
    readonly ServiceFixture _f;

    public OrchestratorTests() => _f = new ServiceFixture(chat: _chat);

    public void Dispose()
    {
        _f.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public void Json_response_format_only_for_openai_and_azure()
    {
        Assert.Equal(ChatResponseFormat.Json, ChatOptionsFactory.CreateJsonOptions(LlmProvider.OpenAI, "m").ResponseFormat);
        Assert.Equal(ChatResponseFormat.Json, ChatOptionsFactory.CreateJsonOptions(LlmProvider.AzureOpenAI, "m").ResponseFormat);
        Assert.Null(ChatOptionsFactory.CreateJsonOptions(LlmProvider.Anthropic, "m").ResponseFormat);
        Assert.Null(ChatOptionsFactory.CreateJsonOptions(LlmProvider.GoogleAI, "m").ResponseFormat);
    }

    [Fact]
    public async Task LlmCallHelper_repairs_json_and_retries_invalid_responses()
    {
        _chat.EnqueueText("not json at all").EnqueueText("```json\n{\"paragraph_content\": \"Fixed.\",}\n```");
        var messages = new List<ChatMessage> { new(ChatRole.User, "go") };
        var result = await LlmCallHelper.CallLlmWithRetry(_chat, messages, new ChatOptions(), j => j["paragraph_content"]?.ToString(), _f.Log);
        Assert.Equal("Fixed.", result);
        Assert.Equal(2, _chat.Requests.Count);
        Assert.Contains("not valid JSON", _chat.Requests[1][^1].Text);
    }

    [Fact]
    public void JsonRepair_handles_escaped_quotes_and_trailing_commas_outside_strings()
    {
        var repaired = JsonRepairUtility.ExtractAndRepair(
            """{"paragraph_content":"She copied C:\\","note":"The symbol ,] was chalked.","items":[1,]}""");
        using var document = System.Text.Json.JsonDocument.Parse(repaired);

        Assert.Equal("She copied C:\\", document.RootElement.GetProperty("paragraph_content").GetString());
        Assert.Equal("The symbol ,] was chalked.", document.RootElement.GetProperty("note").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("items")[0].GetInt32());
    }

    [Fact]
    public async Task LlmCallHelper_surfaces_authentication_failures()
    {
        _chat.EnqueueFailure(new HttpRequestException("x", null, System.Net.HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<LlmCallException>(() =>
            LlmCallHelper.CallLlmWithRetry(_chat, [new ChatMessage(ChatRole.User, "go")], new ChatOptions(), j => j.ToString(), _f.Log));
    }

    [Fact]
    public async Task LlmCallHelper_logs_no_prose()
    {
        _chat.EnqueueText("{\"paragraph_content\":\"The secret prose.\"}");
        await LlmCallHelper.CallLlmWithRetry(_chat, [new ChatMessage(ChatRole.User, "go")], new ChatOptions(), j => j.ToString(), _f.Log);
        Assert.DoesNotContain(_f.Log.Lines, l => l.Contains("secret prose"));
    }

    [Fact]
    public async Task Unverified_or_missing_client_reasons_surface()
    {
        var orchestrator = new OrchestratorMethods(_f.Log, _f.Embeddings,
            new FakeChatClientProvider(null, "Model 'x' has not passed the capability check. Verify it in Settings."), new StubSettings());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ParseNewStory("T", "S", "x"));
        Assert.Contains("capability check", ex.Message);
    }

    [Fact]
    public async Task ParseNewStory_and_CreateNewChapters_use_the_chosen_model()
    {
        _chat.EnqueueText("""{"characters":[],"locations":[],"timelines":[]}""").EnqueueText("""{"chapter":[{"chapter_synopsis":"Private chapter prose."}]}""");
        var provider = new FakeChatClientProvider(_chat);
        var orchestrator = new OrchestratorMethods(_f.Log, _f.Embeddings, provider, new StubSettings());
        var json = await orchestrator.ParseNewStory("Title", "Synopsis", "chosen-model");
        Assert.Contains("characters", json);
        var chapters = await orchestrator.CreateNewChapters(json, "2", "chosen-model");
        Assert.Contains("chapter", chapters.Text);
        Assert.DoesNotContain(_f.Log.Lines, line => line.Contains("Private chapter prose."));
        Assert.Equal(["chosen-model", "chosen-model"], provider.Models);
        Assert.Equal(ChatResponseFormat.Json, _chat.Options[0]!.ResponseFormat);
    }

    [Fact]
    public async Task CreateNewChapters_ends_its_progress_message_when_the_call_ends()
    {
        _chat.EnqueueText("""{"chapter":[]}""").EnqueueFailure(new InvalidOperationException("provider down"));
        var orchestrator = new OrchestratorMethods(_f.Log, _f.Embeddings, new FakeChatClientProvider(_chat), new StubSettings());
        var shown = new List<OrchestratorMethods.ReadTextEventArgs>();
        var ended = new List<OrchestratorMethods.ReadTextEventArgs>();
        orchestrator.ReadTextEvent += (_, e) => shown.Add(e);
        orchestrator.ReadTextEnded += (_, e) =>
        {
            Assert.Contains(e, shown);
            ended.Add(e);
        };

        await orchestrator.CreateNewChapters("{}", "1", "m");
        Assert.Equal("Calling AI provider...", Assert.Single(shown).Message);
        Assert.Same(shown[0], Assert.Single(ended));

        // A failed call closes its message too.
        await Assert.ThrowsAnyAsync<Exception>(() => orchestrator.CreateNewChapters("{}", "1", "m"));
        Assert.Equal(2, ended.Count);
        Assert.Same(shown[1], ended[1]);
    }

    [Fact]
    public async Task AddStory_writes_files_from_the_model_output()
    {
        _chat.EnqueueText("""
            {"characters":[{"name":"Ada","descriptions":[{"description_type":"History","description":"Keeps the light.","timeline_name":"Storm"}]}],
             "locations":[{"name":"Lighthouse","descriptions":["A stone tower."]}],
             "timelines":[{"name":"Storm","description":"Wind."}]}
            """).EnqueueText("""
            {"chapter":[{"chapter_name":"One","chapter_synopsis":"The storm arrives.","paragraphs":[{"sequence":1,"contents":"Ada climbed.","location_name":"Lighthouse","timeline_name":"Storm","character_names":["Ada"]}]}]}
            """);
        await _f.Service.AddStory(new Story { Title = "Keeper", Style = "Drama", Theme = "T", Synopsis = "S", ChapterCount = "1" }, "test-model");

        var story = new Story { Title = "Keeper" };
        Assert.Equal("Ada", Assert.Single(_f.Service.GetCharacters(story)).CharacterName);
        Assert.Equal("Storm", Assert.Single(_f.Service.GetTimelines(story)).TimelineName);
        var chapter = Assert.Single(_f.Service.GetChapters(story));
        Assert.Equal("The storm arrives.", chapter.Synopsis);
        Assert.Equal("Ada climbed.", Assert.Single(_f.Service.GetParagraphs(chapter)).ParagraphContent);
        Assert.Contains("Keeper", _f.Persistence.Dirty);
    }

    [Fact]
    public async Task WriteParagraph_returns_the_paragraph_content()
    {
        _chat.EnqueueText("""{"paragraph_content":"Freya turned the necklace over."}""");
        var master = new JSONMasterStory { StoryTitle = "T", CharacterList = [], PreviousParagraphs = [], RelatedParagraphs = [], WorldFacts = [] };
        var text = await _f.Orchestrator.WriteParagraph(master, new AIPrompt { AIPromptText = "go", NumberOfWords = "100", ModelId = "test-model" }, "test-model");
        Assert.Equal("Freya turned the necklace over.", text);
        Assert.Contains("100", _chat.Requests[0].Last().Text);
    }

    [Fact]
    public async Task WriteParagraph_sends_chapter_metadata_without_its_paragraphs()
    {
        _chat.EnqueueText("""{"paragraph_content":"Generated."}""");
        var master = new JSONMasterStory
        {
            StoryTitle = "T",
            CurrentChapter = new JSONChapter
            {
                chapter_name = "Chapter 1",
                chapter_synopsis = "The setup.",
                paragraphs = [new JSONParagraphs { contents = "Future section prose." }]
            },
            CharacterList = [],
            PreviousParagraphs = [],
            RelatedParagraphs = [],
            WorldFacts = []
        };

        await _f.Orchestrator.WriteParagraph(master, new AIPrompt { AIPromptText = "go", NumberOfWords = "100", ModelId = "test-model" }, "test-model");

        var prompt = _chat.Requests[0].Last().Text;
        Assert.Contains("Chapter 1", prompt);
        Assert.Contains("The setup.", prompt);
        Assert.DoesNotContain("Future section prose.", prompt);
    }

    [Fact]
    public void TrimToFit_estimates_current_chapter_metadata_without_paragraph_prose()
    {
        var story = new JSONMasterStory
        {
            StoryTitle = "T",
            CurrentChapter = new JSONChapter
            {
                chapter_name = "Chapter 1",
                chapter_synopsis = "The setup.",
                paragraphs = [new JSONParagraphs { contents = string.Join(" ", Enumerable.Repeat("chapter", 10000)) }]
            },
            PreviousParagraphs = [new JSONParagraphs { contents = "Keep this context.", relevance_score = 1 }],
            RelatedParagraphs = [],
            WorldFacts = [],
            CharacterList = []
        };

        var trimmed = new MasterStoryBuilder("gpt-3.5-turbo").TrimToFit(story, "system", "user");

        Assert.Single(trimmed.PreviousParagraphs);
    }

    [Fact]
    public async Task DetectCharacters_and_attributes()
    {
        _chat.EnqueueText("""{"characters":[{"name":"Freya"},{"name":"Odin"}]}""")
             .EnqueueText("""{"characters":[{"name":"Odin","descriptions":[{"description_type":"Appearance","description":"One eye."}]}]}""");
        var paragraph = new Paragraph { ParagraphContent = "Freya met Odin.", Timeline = new Timeline { TimelineName = "" }, Location = new Location(), Characters = [] };
        var found = await _f.Orchestrator.DetectCharacters(paragraph);
        Assert.Equal(["Freya", "Odin"], found.Select(c => c.CharacterName));
        var attributes = await _f.Orchestrator.DetectCharacterAttributes(paragraph, [new Character { CharacterName = "Odin", CharacterBackground = [] }], "New Character");
        Assert.NotEmpty(attributes);
        Assert.Contains(attributes, a => a.CharacterValue.Contains("Odin") && a.CharacterValue.Contains("One eye."));
    }

    [Fact]
    public async Task GetStoryBeats_returns_text()
    {
        _chat.EnqueueText("Balt holds the ridge.");
        Assert.Equal("Balt holds the ridge.", await _f.Orchestrator.GetStoryBeats("Balt and his father held the ridge."));
    }

    [Fact]
    public async Task Manuscript_methods_parse_their_json()
    {
        _chat.EnqueueText("""{"chapters":[{"index":1,"title":"The Ridge","headingText":"Balt and his father","startText":"Balt","endText":"dusk."}]}""");
        var chapters = await _f.Orchestrator.ParseChaptersFromTextAsync("Balt and his father held the ridge until dusk.");
        Assert.Equal("The Ridge", Assert.Single(chapters).Title);

        _chat.EnqueueText("""{"characters":[{"name":"Balt","backstory":"A son.","backgrounds":[{"backgroundType":"History","timelineName":"Years Before","description":"Fought."}]}]}""");
        var characters = await _f.Orchestrator.ExtractCharactersFromSummaryAsync("Balt fought.", "The Ridge");
        Assert.Equal("Balt", Assert.Single(characters).Name);

        _chat.EnqueueText("""{"locations":[{"name":"Ridge","description":"Windy."}]}""");
        Assert.Equal("Ridge", Assert.Single(await _f.Orchestrator.ExtractLocationsFromSummaryAsync("At the ridge.", "The Ridge")).Name);

        _chat.EnqueueText("""{"timelines":[{"name":"Years Before","description":"Long ago."}]}""");
        Assert.Equal("Years Before", Assert.Single(await _f.Orchestrator.ExtractTimelinesFromSummaryAsync("Long ago.", "The Ridge", 1)).Name);
    }

    [Fact]
    public async Task CreateMasterStory_finds_related_paragraphs_in_earlier_chapters()
    {
        var story = (await _f.Service.GetStorysAsync()).Single();
        var chapter2 = new Chapter { ChapterName = "Chapter 2", Sequence = 2, Synopsis = "x", Story = story };
        chapter2.Paragraph = _f.Service.GetParagraphs(chapter2);
        var paragraph = chapter2.Paragraph[1];
        var before = chapter2.Paragraph.Where(p => p.Sequence < paragraph.Sequence).ToList();

        // The prompt text equals an earlier paragraph, so its vector matches exactly.
        var master = await _f.Service.CreateMasterStory(chapter2, paragraph, _f.Service.GetCharacters(story), before,
            new AIPrompt { AIPromptText = "When the smoke cleared, Father was gone.", NumberOfWords = "100", ModelId = "test-model" });

        Assert.Equal("Forgetting Baldur", master.StoryTitle);
        Assert.Equal(["Ravens carry messages for Odin."], master.WorldFacts);
        Assert.Single(master.PreviousParagraphs);
        Assert.Equal("When the smoke cleared, Father was gone.", master.RelatedParagraphs[0].contents);
        Assert.InRange(master.RelatedParagraphs.Count, 1, 10);
        Assert.False(string.IsNullOrEmpty(master.TimelineSummary));
    }

    [Fact]
    public void TrimToFit_drops_knowledge_base_notes_first()
    {
        var builder = new MasterStoryBuilder("gpt-3.5-turbo");
        var bigParagraph = new JSONParagraphs { contents = string.Join(" ", Enumerable.Repeat("word", 600)), relevance_score = 1 };
        var story = new JSONMasterStory
        {
            StoryTitle = "T",
            KnowledgeBaseNotes = string.Join(" ", Enumerable.Repeat("note", 4000)),
            PreviousParagraphs = Enumerable.Range(0, 20).Select(_ => bigParagraph).ToList(),
            RelatedParagraphs = [],
            WorldFacts = [],
            CharacterList = [],
        };
        var trimmed = builder.TrimToFit(story, "system", "user");
        Assert.Null(trimmed.KnowledgeBaseNotes);
        Assert.NotEmpty(trimmed.PreviousParagraphs);

        var small = new JSONMasterStory { StoryTitle = "T", KnowledgeBaseNotes = "Hrafnkel forged it. [[Lore/Necklace]]", PreviousParagraphs = [], RelatedParagraphs = [], WorldFacts = [], CharacterList = [] };
        Assert.Equal("Hrafnkel forged it. [[Lore/Necklace]]", builder.TrimToFit(small, "system", "user").KnowledgeBaseNotes);
    }
}
