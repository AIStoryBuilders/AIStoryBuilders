using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using AIStoryBuilders.Services;
using AIStoryBuilders.Model;
using System.Net;

namespace AIStoryBuilders.AI;

/// <summary>
/// Raised when an LLM call fails in a way the caller should surface rather than
/// silently treat as an empty result (e.g. authentication failures). When
/// <see cref="IsAuthenticationError"/> is set the provider rejected the
/// credentials, which is not retryable and must abort the operation.
/// </summary>
public class LlmCallException : Exception
{
    public bool IsAuthenticationError { get; }

    public LlmCallException(string message, bool isAuthenticationError, Exception innerException = null)
        : base(message, innerException)
    {
        IsAuthenticationError = isAuthenticationError;
    }
}

/// <summary>
/// Wraps IChatClient calls with retry logic and JSON validation.
/// Used by all OrchestratorMethods that expect structured JSON output.
/// </summary>
public static class LlmCallHelper
{
    private const int MaxRetries = 2;

    /// <summary>
    /// Detects whether an exception thrown by an <see cref="IChatClient"/>
    /// indicates the provider rejected the API key / credentials. Such failures
    /// are permanent for the run, so they must not be retried and should be
    /// surfaced to the user rather than swallowed as an empty result.
    /// </summary>
    internal static bool IsAuthenticationFailure(Exception ex)
    {
        for (Exception e = ex; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException httpEx
                && (httpEx.StatusCode == HttpStatusCode.Unauthorized
                    || httpEx.StatusCode == HttpStatusCode.Forbidden))
                return true;

            // OpenAI/Azure throw System.ClientModel.ClientResultException, whose
            // Status property carries the HTTP code; match without a hard dependency.
            var statusProp = e.GetType().GetProperty("Status");
            if (statusProp?.GetValue(e) is int status && (status == 401 || status == 403))
                return true;

            var msg = e.Message;
            if (!string.IsNullOrEmpty(msg) &&
                (msg.Contains("invalid x-api-key", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("authentication_error", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("Incorrect API key", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("401", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the expected JSON array whether the model wrapped it in an object
    /// (<c>{"items": [...]}</c>) or returned a bare top-level array (<c>[...]</c>).
    /// Lesser models frequently emit a bare array; indexing a <see cref="JArray"/>
    /// by a string property name otherwise throws.
    /// </summary>
    public static JArray ExtractNamedArray(JToken node, string propertyName)
    {
        if (node is JArray topLevel) return topLevel;
        if (node is JObject obj && obj[propertyName] is JArray arr) return arr;
        return null;
    }

    /// <summary>
    /// Call the LLM and parse/validate the JSON response.
    /// On failure, appends an error-context message and retries.
    /// Authentication failures are surfaced immediately as a
    /// <see cref="LlmCallException"/> rather than retried.
    /// </summary>
    public static async Task<T> CallLlmWithRetry<T>(
        IChatClient client,
        List<ChatMessage> messages,
        ChatOptions options,
        Func<JToken, T> mapResult,
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

                // Step 2: Parse JSON (JToken tolerates a bare top-level array)
                var token = JToken.Parse(repairedJson);

                // Step 3: Map to result type
                return mapResult(token);
            }
            catch (Exception ex) when (IsAuthenticationFailure(ex))
            {
                // Credentials were rejected — not retryable. Surface immediately
                // so the caller reports an error instead of an empty result.
                logService.WriteToLog($"LLM authentication failed: {ex.Message}");
                throw new LlmCallException(
                    $"AI provider authentication failed: {ex.Message}", true, ex);
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
    /// Authentication failures are surfaced immediately as a
    /// <see cref="LlmCallException"/> rather than retried.
    /// </summary>
    public static async Task<string> CallLlmForText(
        IChatClient client,
        List<ChatMessage> messages,
        ChatOptions options,
        LogService logService)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
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
            catch (Exception ex) when (IsAuthenticationFailure(ex))
            {
                logService.WriteToLog($"LLM authentication failed: {ex.Message}");
                throw new LlmCallException(
                    $"AI provider authentication failed: {ex.Message}", true, ex);
            }
            catch (Exception ex)
            {
                logService.WriteToLog(
                    $"LLM text retry {attempt + 1}/{MaxRetries + 1}: {ex.Message}");
                if (attempt == MaxRetries) break;
            }
        }

        return "";
    }
}
