using AIStoryBuildersV2.Shared.Llm;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

public class ChatClientFactoryTests
{
    static readonly HttpClient Http = new();

    static IChatClient Create(LlmProvider provider, string model = "m", string? endpoint = null, string? key = "k") =>
        ChatClientFactory.Create(new LlmOptions(provider, model, endpoint, key), Http, isBrowser: false);

    [Fact]
    public void Anthropic_UsesTheCustomClient()
    {
        var client = Create(LlmProvider.Anthropic, "claude-sonnet-5-5", key: "sk-ant");
        Assert.IsType<AnthropicChatClient>(client);
        Assert.Equal("claude-sonnet-5-5", client.GetService<ChatClientMetadata>()?.DefaultModelId);
    }

    [Fact]
    public void OpenAI_CreatesAClient()
    {
        var client = Create(LlmProvider.OpenAI, "gpt-4.1", key: "sk-openai");
        Assert.NotNull(client);
        Assert.Equal("gpt-4.1", client.GetService<ChatClientMetadata>()?.DefaultModelId);
    }

    [Fact]
    public void AzureOpenAI_CreatesAClient()
    {
        var client = Create(LlmProvider.AzureOpenAI, "my-deployment", "https://example.openai.azure.com/", "azure-key");
        Assert.NotNull(client);
    }

    [Fact]
    public void Ollama_CreatesAClient_WithoutAKey()
    {
        var client = Create(LlmProvider.Ollama, "llama3.2", "http://localhost:11434/", key: null);
        Assert.NotNull(client);
        Assert.Equal("llama3.2", client.GetService<ChatClientMetadata>()?.DefaultModelId);
    }

    [Theory]
    [InlineData(LlmProvider.Anthropic, null)]
    [InlineData(LlmProvider.Anthropic, "   ")]
    [InlineData(LlmProvider.OpenAI, null)]
    [InlineData(LlmProvider.AzureOpenAI, "")]
    public void MissingKey_Throws(LlmProvider provider, string? key)
    {
        var ex = Assert.Throws<ArgumentException>(() => Create(provider, "m", "https://example.openai.azure.com/", key));
        Assert.Contains("API key", ex.Message);
    }

    [Theory]
    [InlineData(LlmProvider.AzureOpenAI, null)]
    [InlineData(LlmProvider.AzureOpenAI, "not a url")]
    [InlineData(LlmProvider.Ollama, null)]
    [InlineData(LlmProvider.Ollama, "")]
    [InlineData(LlmProvider.Ollama, "ftp://host/")]
    public void MissingOrInvalidEndpoint_Throws(LlmProvider provider, string? endpoint)
    {
        Assert.Throws<ArgumentException>(() => Create(provider, "m", endpoint, "k"));
    }

    [Theory]
    [InlineData(LlmProvider.Anthropic)]
    [InlineData(LlmProvider.OpenAI)]
    [InlineData(LlmProvider.Ollama)]
    public void MissingModel_Throws(LlmProvider provider)
    {
        Assert.Throws<ArgumentException>(() => Create(provider, "  ", "http://localhost:11434/", "k"));
    }

    [Fact]
    public void NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ChatClientFactory.Create(null!, Http, false));
    }

#if DEBUG
    [Fact]
    public void Fake_ExistsInDebugBuilds()
    {
        Assert.IsType<FakeChatClient>(Create(LlmProvider.Fake, "", key: null));
        Assert.Contains(LlmProvider.Fake, LlmDefaults.Available());
    }
#else
    [Fact]
    public void Fake_IsNotAvailableInReleaseBuilds()
    {
        Assert.Throws<ArgumentException>(() => Create(LlmProvider.Fake, "", key: null));
        Assert.DoesNotContain(LlmProvider.Fake, LlmDefaults.Available());
    }
#endif

    [Fact]
    public void Defaults()
    {
        Assert.Equal(LlmProvider.Anthropic, LlmDefaults.Provider);
        Assert.Equal("claude-sonnet-5-5", LlmDefaults.DefaultModel(LlmProvider.Anthropic));
        Assert.Equal("api.anthropic.com", LlmDefaults.HostFor(LlmProvider.Anthropic, null));
        Assert.Equal("example.com:8080", LlmDefaults.HostFor(LlmProvider.Ollama, "http://example.com:8080/"));
        Assert.Null(LlmDefaults.HostFor(LlmProvider.Ollama, null));
        Assert.True(LlmDefaults.RequiresKey(LlmProvider.AzureOpenAI));
        Assert.False(LlmDefaults.RequiresKey(LlmProvider.Ollama));
        Assert.True(LlmDefaults.RequiresEndpoint(LlmProvider.Ollama));
        Assert.False(LlmDefaults.RequiresEndpoint(LlmProvider.Anthropic));
    }
}
