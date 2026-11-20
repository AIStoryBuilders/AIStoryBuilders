using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace AIStoryBuildersV2.Shared.Llm;

/// <summary>
/// Plan §6.3, D8, D34. Fake exists only in DEBUG builds. Settings store the provider by name; new providers are still
/// appended so numeric values never shift.
/// </summary>
public enum LlmProvider { Anthropic, OpenAI, AzureOpenAI, GoogleAI, Ollama, Fake, DataExchange }

public sealed record LlmOptions(LlmProvider Provider, string Model, string? Endpoint, string? ApiKey);

public static class LlmDefaults
{
    public const LlmProvider Provider = LlmProvider.Anthropic;
    public const string AnthropicModel = "claude-sonnet-5-5";

    public static string DefaultModel(LlmProvider provider) => provider switch
    {
        LlmProvider.Anthropic => AnthropicModel,
        LlmProvider.OpenAI => "gpt-4.1",
        LlmProvider.AzureOpenAI => "",
        // D8: no Google AI default; the user picks from the list.
        LlmProvider.GoogleAI => "",
        LlmProvider.Ollama => "llama3.2",
        // D34: the connect flow's recommended model, or the user's pick from the gateway's list.
        LlmProvider.DataExchange => "",
        _ => "fake",
    };

    public const string GoogleAIHost = "generativelanguage.googleapis.com";

    /// <summary>D34: the Blazor DataExchange gateway's network timeout. Chat calls are never retried (a retried POST bills twice).</summary>
    public static readonly TimeSpan DataExchangeNetworkTimeout = TimeSpan.FromMinutes(5);

    public static bool RequiresKey(LlmProvider provider) =>
        provider is LlmProvider.Anthropic or LlmProvider.OpenAI or LlmProvider.AzureOpenAI or LlmProvider.GoogleAI or LlmProvider.DataExchange;

    /// <summary>
    /// The settings keep an endpoint only for these providers. DataExchange's endpoint is the gateway base URL (ending in
    /// <c>/v1/</c>), which comes from the connect flow or configuration rather than being typed (D34).
    /// </summary>
    public static bool RequiresEndpoint(LlmProvider provider) => provider is LlmProvider.AzureOpenAI or LlmProvider.Ollama or LlmProvider.DataExchange;

    public static string DisplayName(LlmProvider provider) => provider switch
    {
        LlmProvider.AzureOpenAI => "Azure OpenAI",
        LlmProvider.GoogleAI => "Google AI",
        LlmProvider.DataExchange => "Blazor DataExchange",
        _ => provider.ToString(),
    };

    /// <summary>The only host the API key is sent to.</summary>
    public static string? HostFor(LlmProvider provider, string? endpoint) => provider switch
    {
        LlmProvider.Anthropic => "api.anthropic.com",
        LlmProvider.OpenAI => "api.openai.com",
        LlmProvider.GoogleAI => GoogleAIHost,
        LlmProvider.AzureOpenAI or LlmProvider.Ollama or LlmProvider.DataExchange => Uri.TryCreate(endpoint, UriKind.Absolute, out var u) ? u.Authority : null,
        _ => null,
    };

    /// <summary>In the Settings dropdown's order (UI-Behavior-Spec §6.1).</summary>
    public static IEnumerable<LlmProvider> Available()
    {
        yield return LlmProvider.DataExchange;
        yield return LlmProvider.OpenAI;
        yield return LlmProvider.AzureOpenAI;
        yield return LlmProvider.Anthropic;
        yield return LlmProvider.GoogleAI;
        yield return LlmProvider.Ollama;
#if DEBUG
        yield return LlmProvider.Fake;
#endif
    }
}

/// <summary>
/// Creates provider clients that work both in the browser and on the desktop (KGB, D8). Callers wrap the result in
/// <see cref="AI.TemperatureFallbackChatClient"/> (D10).
/// </summary>
public static class ChatClientFactory
{
    /// <exception cref="ArgumentException">A required value is missing (key; endpoint for Azure, Ollama and DataExchange).</exception>
    public static IChatClient Create(LlmOptions options, HttpClient http, bool isBrowser)
    {
        ArgumentNullException.ThrowIfNull(options);
        var model = options.Model?.Trim();
        if (string.IsNullOrEmpty(model) && options.Provider != LlmProvider.Fake)
            throw new ArgumentException(options.Provider == LlmProvider.AzureOpenAI ? "Enter the deployment name." : "Enter a model.");
        if (LlmDefaults.RequiresKey(options.Provider) && string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("Add an API key in Settings");

        Uri? endpoint = null;
        if (LlmDefaults.RequiresEndpoint(options.Provider))
        {
            if (!Uri.TryCreate(options.Endpoint?.Trim(), UriKind.Absolute, out endpoint) || endpoint.Scheme is not ("http" or "https"))
                throw new ArgumentException("Enter a valid endpoint URL.");
        }

        var key = options.ApiKey?.Trim() ?? "";
        switch (options.Provider)
        {
            case LlmProvider.Anthropic:
                return new AnthropicChatClient(http, key, model!, browserAccess: isBrowser);

            case LlmProvider.OpenAI:
                return new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) })
                    .GetChatClient(model).AsIChatClient();

            case LlmProvider.AzureOpenAI:
                return new AzureOpenAIClient(endpoint!, new ApiKeyCredential(key), new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) })
                    .GetChatClient(model).AsIChatClient();

            case LlmProvider.GoogleAI:
                return new GoogleAIChatClient(http, key, model!);

            case LlmProvider.DataExchange:
                // D34: an OpenAI-compatible gateway. No client retries (a retried POST is billed twice) and a long timeout.
                return new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions
                    {
                        Endpoint = endpoint,
                        Transport = new HttpClientPipelineTransport(LongRunning(http)),
                        NetworkTimeout = LlmDefaults.DataExchangeNetworkTimeout,
                        RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                    })
                    .GetChatClient(model).AsIChatClient();

            case LlmProvider.Ollama:
                return new OllamaApiClient(new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromMinutes(5) }, model!);

#if DEBUG
            case LlmProvider.Fake:
                return new FakeChatClient();
#endif
            default:
                throw new ArgumentException($"The provider {options.Provider} isn't available in this build.");
        }
    }

    /// <summary>
    /// The pipeline's <see cref="OpenAIClientOptions.NetworkTimeout"/> can't outlast <see cref="HttpClient.Timeout"/>, and the
    /// browser host's shared client keeps the 100-second default. A shorter client is replaced by one with no timeout of its
    /// own, so the 5-minute network timeout applies (D34). The desktop host's 5-minute client is used as is.
    /// </summary>
    internal static HttpClient LongRunning(HttpClient http) =>
        http.Timeout == Timeout.InfiniteTimeSpan || http.Timeout >= LlmDefaults.DataExchangeNetworkTimeout
            ? http
            : new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
}
