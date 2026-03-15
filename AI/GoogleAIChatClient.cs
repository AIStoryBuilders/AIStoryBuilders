using Mscc.GenerativeAI;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI;

/// <summary>
/// Thin IChatClient wrapper around Mscc.GenerativeAI (Google Gemini).
/// Translates Microsoft.Extensions.AI calls into GenerativeModel calls.
/// </summary>
public sealed class GoogleAIChatClient : IChatClient
{
    private readonly GenerativeModel _model;
    private readonly string _modelId;

    public GoogleAIChatClient(string apiKey, string model)
    {
        _modelId = model;
        var googleAI = new GoogleAI(apiKey: apiKey);
        _model = googleAI.GenerativeModel(model: model);
    }

    public ChatClientMetadata Metadata =>
        new(nameof(GoogleAIChatClient), null, _modelId);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Extract system instruction and build content list
        string systemInstruction = null;
        var parts = new List<string>();

        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System)
            {
                systemInstruction = msg.Text;
            }
            else
            {
                parts.Add(msg.Text);
            }
        }

        // Prepend system instruction to the first user turn if present
        var prompt = systemInstruction != null
            ? $"{systemInstruction}\n\n{string.Join("\n", parts)}"
            : string.Join("\n", parts);

        // 2. Call Gemini SDK
        var response = await _model.GenerateContent(prompt);

        // 3. Map response to Microsoft.Extensions.AI.ChatResponse
        var text = response?.Text ?? "";

        var usage = response?.UsageMetadata;

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = usage?.PromptTokenCount,
                OutputTokenCount = usage?.CandidatesTokenCount,
                TotalTokenCount = usage?.TotalTokenCount
                                ?? (usage?.PromptTokenCount ?? 0)
                                 + (usage?.CandidatesTokenCount ?? 0)
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
        => serviceType == typeof(GoogleAIChatClient) ? this : null;

    public void Dispose() { /* GoogleAI client has no Dispose */ }
}
