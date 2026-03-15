using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI;

/// <summary>
/// Estimates token count using a character-count heuristic.
/// </summary>
public static class TokenEstimator
{
    private const double CharsPerToken = 4.0;

    private static readonly Dictionary<string, int> ModelContextWindows = new()
    {
        // OpenAI
        ["gpt-3.5-turbo"] = 16_385,
        ["gpt-4o"] = 128_000,
        ["gpt-4o-mini"] = 128_000,
        ["GPT-4.1"] = 1_047_576,
        ["gpt-4.1"] = 1_047_576,
        ["gpt-5-mini"] = 1_047_576,
        ["gpt-5"] = 1_047_576,
        // Anthropic
        ["claude-3-5-sonnet-20241022"] = 200_000,
        ["claude-3-5-haiku-20241022"] = 200_000,
        ["claude-4-sonnet"] = 200_000,
        // Google
        ["gemini-2.0-flash"] = 1_048_576,
        ["gemini-2.5-pro"] = 1_048_576,
    };

    private const int DefaultContextWindow = 128_000;
    private const double PromptBudgetRatio = 0.75;

    public static int EstimateTokens(string text)
        => (int)Math.Ceiling((text?.Length ?? 0) / CharsPerToken);

    public static int EstimateTokens(IEnumerable<ChatMessage> messages)
        => messages.Sum(m => EstimateTokens(m.Text ?? ""));

    public static int GetMaxPromptTokens(string modelId)
    {
        var window = ModelContextWindows.GetValueOrDefault(modelId, DefaultContextWindow);
        return (int)(window * PromptBudgetRatio);
    }
}
