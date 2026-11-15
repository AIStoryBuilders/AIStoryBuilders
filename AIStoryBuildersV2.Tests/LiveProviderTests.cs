using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Models.JSON;
using AIStoryBuildersV2.Shared.Stories;

namespace AIStoryBuildersV2.Tests;

/// <summary>
/// Opt-in tests against a real provider (Dev-Environment §6). They skip unless ASB_LIVE_PROVIDER, ASB_LIVE_KEY and
/// ASB_LIVE_MODEL are set (ASB_LIVE_ENDPOINT for Azure and Ollama). Keys never appear in code or output.
/// </summary>
public class LiveProviderTests
{
    static LlmOptions? Live()
    {
        var provider = Environment.GetEnvironmentVariable("ASB_LIVE_PROVIDER");
        var model = Environment.GetEnvironmentVariable("ASB_LIVE_MODEL");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model) || !Enum.TryParse<LlmProvider>(provider, true, out var p)) return null;
        return new LlmOptions(p, model, Environment.GetEnvironmentVariable("ASB_LIVE_ENDPOINT"), Environment.GetEnvironmentVariable("ASB_LIVE_KEY"));
    }

    [SkippableFact]
    public async Task Live_model_passes_the_probe()
    {
        var options = Live();
        Skip.If(options is null, "Set ASB_LIVE_PROVIDER, ASB_LIVE_KEY and ASB_LIVE_MODEL to run live tests.");
        var result = await new ModelCapabilityProbe(new HttpClient(), new TestHost("")).ProbeAsync(options!, default);
        Assert.True(result.Passed, $"{result.FailedAt}: {result.Message}");
    }

    [SkippableFact]
    public async Task Live_WriteParagraph_returns_prose()
    {
        var options = Live();
        Skip.If(options is null, "Set ASB_LIVE_PROVIDER, ASB_LIVE_KEY and ASB_LIVE_MODEL to run live tests.");
        var client = new TemperatureFallbackChatClient(ChatClientFactory.Create(options!, new HttpClient(), isBrowser: false));
        using var f = new ServiceFixture(chat: client);
        var orchestrator = new OrchestratorMethods(f.Log, f.Embeddings, new FakeChatClientProvider(client),
            new StubSettings(options!.Provider, options.Model));

        var master = new JSONMasterStory
        {
            StoryTitle = "Forgetting Baldur", StoryStyle = "Fantasy", StorySynopsis = "Balt searches for his father.",
            SystemMessage = "You are a software program that creates prose for novels.",
            CharacterList = [], PreviousParagraphs = [], RelatedParagraphs = [], WorldFacts = ["Ravens carry messages for Odin."],
        };
        var text = await orchestrator.WriteParagraph(master, new AIPrompt { AIPromptText = "Balt reaches the ridge at dawn.", NumberOfWords = "50", ModelId = options.Model }, options.Model);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }
}
