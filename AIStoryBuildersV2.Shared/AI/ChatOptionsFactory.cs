#nullable disable
using AIStoryBuildersV2.Shared.Llm;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.AI;
/// <summary>
/// Build ChatOptions with response_format: json_object for providers that support it.
/// </summary>
public static class ChatOptionsFactory
{
    public static ChatOptions CreateJsonOptions(
        LlmProvider provider,
        string modelId = null,
        float? temperature = 0.1f,
        int? maxOutputTokens = 4096)
    {
        var options = new ChatOptions
        {
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens
        };

        if (modelId != null)
            options.ModelId = modelId;

        // OpenAI and Azure OpenAI support response_format natively
        // D31: switch on the provider instead of V1's AIType strings.
        if (provider is LlmProvider.OpenAI or LlmProvider.AzureOpenAI)
        {
            options.ResponseFormat = ChatResponseFormat.Json;
        }

        // Anthropic and Google AI: no response_format equivalent.
        // JSON enforcement relies on the system prompt + retry loop.

        return options;
    }
}
