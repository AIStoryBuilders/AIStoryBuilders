using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI;

/// <summary>
/// Build ChatOptions with response_format: json_object for providers that support it.
/// </summary>
public static class ChatOptionsFactory
{
    public static ChatOptions CreateJsonOptions(
        string aiServiceType,
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
        if (aiServiceType is "OpenAI" or "Azure OpenAI")
        {
            options.ResponseFormat = ChatResponseFormat.Json;
        }

        // Anthropic and Google AI: no response_format equivalent.
        // JSON enforcement relies on the system prompt + retry loop.

        return options;
    }
}
