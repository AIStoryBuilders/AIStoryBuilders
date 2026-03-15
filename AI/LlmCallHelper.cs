using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using AIStoryBuilders.Services;
using AIStoryBuilders.Model;

namespace AIStoryBuilders.AI;

/// <summary>
/// Wraps IChatClient calls with retry logic and JSON validation.
/// Used by all OrchestratorMethods that expect structured JSON output.
/// </summary>
public static class LlmCallHelper
{
    private const int MaxRetries = 2;

    /// <summary>
    /// Call the LLM and parse/validate the JSON response.
    /// On failure, appends an error-context message and retries.
    /// </summary>
    public static async Task<T> CallLlmWithRetry<T>(
        IChatClient client,
        List<ChatMessage> messages,
        ChatOptions options,
        Func<JObject, T> mapResult,
        LogService logService) where T : class
    {
        string lastError = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await client.GetResponseAsync(messages, options);

                logService.WriteToLog(
                    $"TotalTokens: {response.Usage?.TotalTokenCount} " +
                    $"- Attempt {attempt + 1} - {response.Text}");

                var rawText = response.Text ?? "";

                // Step 1: Deterministic repair
                var repairedJson = JsonRepairUtility.ExtractAndRepair(rawText);

                // Step 2: Parse JSON
                var jObj = JObject.Parse(repairedJson);

                // Step 3: Map to result type
                return mapResult(jObj);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                logService.WriteToLog(
                    $"LLM retry {attempt + 1}/{MaxRetries + 1}: {ex.Message}");

                if (attempt < MaxRetries)
                {
                    messages.Add(new ChatMessage(ChatRole.User,
                        $"Your previous response was not valid JSON. " +
                        $"Error: {ex.Message}. " +
                        $"Please output ONLY the JSON object with no commentary."));
                }
            }
        }

        logService.WriteToLog($"LLM call failed after {MaxRetries + 1} attempts: {lastError}");
        return null;
    }

    /// <summary>
    /// Simplified overload for text-only responses (e.g., GetStoryBeats).
    /// </summary>
    public static async Task<string> CallLlmForText(
        IChatClient client,
        List<ChatMessage> messages,
        ChatOptions options,
        LogService logService)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var response = await client.GetResponseAsync(messages, options);
            var text = response.Text ?? "";

            logService.WriteToLog(
                $"TotalTokens: {response.Usage?.TotalTokenCount} - Text response");

            if (!string.IsNullOrWhiteSpace(text))
                return text;

            messages.Add(new ChatMessage(ChatRole.User,
                "Your previous response was empty. Please provide the requested output."));
        }

        return "";
    }
}
