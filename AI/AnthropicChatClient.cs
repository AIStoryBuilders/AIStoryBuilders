using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI;

/// <summary>
/// Thin IChatClient wrapper around Anthropic.SDK.
/// Translates Microsoft.Extensions.AI calls into Anthropic MessageClient calls.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    private readonly AnthropicClient _client;
    private readonly string _model;

    public AnthropicChatClient(string apiKey, string model)
    {
        _client = new AnthropicClient(apiKey);
        _model = model;
    }

    public ChatClientMetadata Metadata =>
        new(nameof(AnthropicChatClient), null, _model);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Separate system message from conversation messages
        string systemPrompt = null;
        var messages = new List<Anthropic.SDK.Messaging.Message>();

        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System)
            {
                systemPrompt = msg.Text;
            }
            else
            {
                var role = msg.Role == ChatRole.Assistant
                    ? RoleType.Assistant
                    : RoleType.User;
                messages.Add(new Anthropic.SDK.Messaging.Message(role, msg.Text));
            }
        }

        // 2. Build Anthropic request
        var request = new MessageParameters
        {
            Model = options?.ModelId ?? _model,
            MaxTokens = options?.MaxOutputTokens ?? 4096,
            System = new List<SystemMessage>
            {
                new SystemMessage(systemPrompt ?? "You are a helpful assistant.")
            },
            Messages = messages
        };

        // 3. Call Anthropic SDK
        var response = await _client.Messages.GetClaudeMessageAsync(
            request, cancellationToken);

        // 4. Map response to Microsoft.Extensions.AI.ChatResponse
        var text = string.Join("", response.Content
            .OfType<Anthropic.SDK.Messaging.TextContent>()
            .Select(c => c.Text));

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = response.Usage?.InputTokens,
                OutputTokenCount = response.Usage?.OutputTokens,
                TotalTokenCount = (response.Usage?.InputTokens ?? 0)
                                + (response.Usage?.OutputTokens ?? 0)
            }
        };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Streaming is not used by AIStoryBuilders.");

    public object GetService(Type serviceType, object key = null)
        => serviceType == typeof(AnthropicChatClient) ? this : null;

    public void Dispose() { /* AnthropicClient has no Dispose */ }
}
