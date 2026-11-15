using System.Net;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Settings;
using RichardSzalay.MockHttp;

namespace AIStoryBuildersV2.Tests;

public class ModelCatalogTests
{
    const string Key = "sk-ant-secret-123456789";

    static LlmOptions Options(LlmProvider provider, string? endpoint = null, string? key = Key) => new(provider, "", endpoint, key);

    [Fact]
    public async Task Anthropic_FollowsPages_AndSendsTheBrowserHeaderOnlyInTheBrowser()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect("https://api.anthropic.com/v1/models?limit=100")
            .WithHeaders("x-api-key", Key).WithHeaders("anthropic-version", "2023-06-01")
            .WithHeaders("anthropic-dangerous-direct-browser-access", "true")
            .Respond("application/json", """{"data":[{"id":"claude-opus-5-5"},{"id":"claude-sonnet-5-5"}],"has_more":true,"last_id":"claude-sonnet-5-5"}""");
        mock.Expect("https://api.anthropic.com/v1/models?limit=100&after_id=claude-sonnet-5-5")
            .Respond("application/json", """{"data":[{"id":"claude-haiku-5-5"}],"has_more":false,"last_id":"claude-haiku-5-5"}""");

        var result = await ModelCatalog.ListAsync(Options(LlmProvider.Anthropic), mock.ToHttpClient(), isBrowser: true, default);

        Assert.Equal(ModelListStatus.Success, result.Status);
        Assert.Equal(["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"], result.Models);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Anthropic_OnTheDesktop_DoesNotSendTheBrowserHeader()
    {
        var mock = new MockHttpMessageHandler();
        HttpRequestMessage? seen = null;
        mock.When("https://api.anthropic.com/v1/models*").Respond(req => { seen = req; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"id":"m"}]}""") }; });

        await ModelCatalog.ListAsync(Options(LlmProvider.Anthropic), mock.ToHttpClient(), isBrowser: false, default);

        Assert.False(seen!.Headers.Contains("anthropic-dangerous-direct-browser-access"));
    }

    [Fact]
    public async Task OpenAI_KeepsChatModels_AndFallsBackToEverything()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://api.openai.com/v1/models").WithHeaders("Authorization", "Bearer " + Key).Respond("application/json",
            """{"data":[{"id":"gpt-4.1"},{"id":"whisper-1"},{"id":"gpt-4o-realtime-preview"},{"id":"o3"},{"id":"tts-1"},{"id":"gpt-image-1"}]}""");

        var result = await ModelCatalog.ListAsync(Options(LlmProvider.OpenAI), mock.ToHttpClient(), false, default);

        Assert.Equal(["o3", "gpt-4.1"], result.Models);
        Assert.Equal(["future-b", "future-a"], ModelCatalog.FilterOpenAIChatModels(["future-a", "future-b"]));
    }

    [Fact]
    public async Task AzureOpenAI_ListsDeployments_ThenTriesTheModelsRoute()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://res.openai.azure.com/openai/deployments*").WithHeaders("api-key", Key).Respond(HttpStatusCode.NotFound);
        mock.When("https://res.openai.azure.com/openai/models*").Respond("application/json", """{"data":[{"name":"chat-prod"},{"id":"chat-dev"}]}""");

        var result = await ModelCatalog.ListAsync(Options(LlmProvider.AzureOpenAI, "https://res.openai.azure.com/some/path"), mock.ToHttpClient(), false, default);

        Assert.Equal(["chat-dev", "chat-prod"], result.Models);
    }

    [Fact]
    public async Task AzureOpenAI_StopsAtARejectedKey()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect("https://res.openai.azure.com/openai/deployments*").Respond(HttpStatusCode.Unauthorized);

        var result = await ModelCatalog.ListAsync(Options(LlmProvider.AzureOpenAI, "https://res.openai.azure.com/"), mock.ToHttpClient(), false, default);

        Assert.Equal(ModelListStatus.InvalidKey, result.Status);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Ollama_ReadsTags_WithoutAKey()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("http://localhost:11434/api/tags").Respond("application/json", """{"models":[{"name":"qwen3:8b"},{"name":"llama3.2:latest"},{"model":"gemma3"}]}""");

        var result = await ModelCatalog.ListAsync(Options(LlmProvider.Ollama, "http://localhost:11434/", key: null), mock.ToHttpClient(), false, default);

        Assert.Equal(["gemma3", "llama3.2:latest", "qwen3:8b"], result.Models);
    }

    [Theory]
    [InlineData(LlmProvider.Anthropic, null, null, "API key")]
    [InlineData(LlmProvider.AzureOpenAI, "not a url", Key, "endpoint")]
    [InlineData(LlmProvider.Ollama, null, null, "endpoint")]
    public async Task MissingValues_AreReportedWithoutARequest(LlmProvider provider, string? endpoint, string? key, string mention)
    {
        var mock = new MockHttpMessageHandler();
        var result = await ModelCatalog.ListAsync(Options(provider, endpoint, key), mock.ToHttpClient(), false, default);

        Assert.Equal(ModelListStatus.NotConfigured, result.Status);
        Assert.Contains(mention, result.Message);
    }

    [Fact]
    public async Task Failures_AreDescribed_WithoutTheKey()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://api.anthropic.com/*").Throw(new HttpRequestException("TypeError: Failed to fetch " + Key));
        var blocked = await ModelCatalog.ListAsync(Options(LlmProvider.Anthropic), mock.ToHttpClient(), isBrowser: true, default);
        Assert.Equal(ModelListStatus.Unreachable, blocked.Status);
        Assert.Contains("blocked", blocked.Message);
        Assert.DoesNotContain(Key, blocked.Message);

        mock.Clear();
        mock.When("https://api.anthropic.com/*").Respond(HttpStatusCode.InternalServerError);
        var server = await ModelCatalog.ListAsync(Options(LlmProvider.Anthropic), mock.ToHttpClient(), false, default);
        Assert.Contains("500", server.Message);

        mock.Clear();
        mock.When("https://api.anthropic.com/*").Respond("application/json", "<html>");
        Assert.Equal(ModelListStatus.Unreachable, (await ModelCatalog.ListAsync(Options(LlmProvider.Anthropic), mock.ToHttpClient(), false, default)).Status);

        mock.Clear();
        mock.When("https://api.anthropic.com/*").Respond("application/json", """{"data":[]}""");
        Assert.Equal(ModelListStatus.Empty, (await ModelCatalog.ListAsync(Options(LlmProvider.Anthropic), mock.ToHttpClient(), false, default)).Status);
    }

    // ---------------------------------------------------------------- through AppSettingsService

    static (AppSettingsService Service, MockHttpMessageHandler Mock) Service()
    {
        var mock = new MockHttpMessageHandler();
        var service = new AppSettingsService(new BrowserSettingsStore(new InMemoryKeyValueStore()), new TestHost("") { IsBrowser = false }, mock.ToHttpClient(), new StubProbe());
        return (service, mock);
    }

    [Fact]
    public async Task Settings_UsesTheSavedKey_AndCachesTheList()
    {
        var (service, mock) = Service();
        await service.SaveAsync(LlmProvider.Anthropic, "claude-x", null, Key, default);
        var call = mock.When("https://api.anthropic.com/v1/models*").WithHeaders("x-api-key", Key).Respond("application/json", """{"data":[{"id":"claude-x"}]}""");

        var first = await service.ListModelsAsync(LlmProvider.Anthropic, null, null, refresh: false, default);
        var second = await service.ListModelsAsync(LlmProvider.Anthropic, null, null, refresh: false, default);
        await service.ListModelsAsync(LlmProvider.Anthropic, null, null, refresh: true, default);

        Assert.Equal(["claude-x"], first.Models);
        Assert.Same(first, second);
        Assert.Equal(2, mock.GetMatchCount(call));
    }

    [Fact]
    public async Task Settings_DoesNotSendTheSavedKeyToAnotherHost()
    {
        var (service, mock) = Service();
        await service.SaveAsync(LlmProvider.Anthropic, "claude-x", null, Key, default);

        var otherProvider = await service.ListModelsAsync(LlmProvider.OpenAI, null, null, false, default);
        Assert.Equal(ModelListStatus.NotConfigured, otherProvider.Status);

        await service.SaveAsync(LlmProvider.AzureOpenAI, "dep", "https://a.openai.azure.com/", null, default);
        var otherEndpoint = await service.ListModelsAsync(LlmProvider.AzureOpenAI, "https://b.openai.azure.com/", null, false, default);
        Assert.Equal(ModelListStatus.NotConfigured, otherEndpoint.Status);

        // A typed key may go anywhere the form points.
        mock.When("https://b.openai.azure.com/openai/deployments*").WithHeaders("api-key", "typed").Respond("application/json", """{"data":[{"id":"dep-b"}]}""");
        var typed = await service.ListModelsAsync(LlmProvider.AzureOpenAI, "https://b.openai.azure.com/", "typed", false, default);
        Assert.Equal(["dep-b"], typed.Models);
    }

    [Fact]
    public void CacheKey_NeverHoldsTheKey()
    {
        var cacheKey = AppSettingsService.ModelCacheKey(new LlmOptions(LlmProvider.Anthropic, "", null, Key));
        Assert.DoesNotContain("secret", cacheKey);
        Assert.NotEqual(cacheKey, AppSettingsService.ModelCacheKey(new LlmOptions(LlmProvider.Anthropic, "", null, Key + "x")));
    }
}
