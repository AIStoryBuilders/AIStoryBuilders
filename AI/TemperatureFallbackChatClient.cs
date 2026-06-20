using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI;

/// <summary>
/// Wraps an <see cref="IChatClient"/> and transparently retries a request
/// without the temperature parameter when the underlying model rejects a
/// non-default temperature. Newer reasoning models (e.g. OpenAI gpt-5.x) and
/// some Anthropic models only accept their default temperature and return an
/// error otherwise; stripping the parameter lets the same call succeed instead
/// of failing every LLM pass and silently producing empty results.
/// </summary>
public sealed class TemperatureFallbackChatClient : DelegatingChatClient
{
    public TemperatureFallbackChatClient(IChatClient innerClient) : base(innerClient) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex) when (IsTemperatureUnsupported(ex, options))
        {
            return await base.GetResponseAsync(
                messages, WithoutTemperature(options), cancellationToken);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator = base
            .GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var retried = false;

        try
        {
            while (true)
            {
                ChatResponseUpdate current;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                    current = enumerator.Current;
                }
                catch (Exception ex) when (!retried && IsTemperatureUnsupported(ex, options))
                {
                    // The model rejected the temperature before emitting any
                    // tokens; restart the stream without it.
                    await enumerator.DisposeAsync();
                    retried = true;
                    enumerator = base
                        .GetStreamingResponseAsync(
                            messages, WithoutTemperature(options), cancellationToken)
                        .GetAsyncEnumerator(cancellationToken);
                    continue;
                }

                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static bool IsTemperatureUnsupported(Exception ex, ChatOptions options)
    {
        if (options?.Temperature is null)
            return false;

        for (Exception e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (!string.IsNullOrEmpty(m)
                && m.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                && (m.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("does not support", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("deprecated", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("only the default", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static ChatOptions WithoutTemperature(ChatOptions options)
    {
        if (options is null)
            return null;
        var clone = options.Clone();
        clone.Temperature = null;
        return clone;
    }
}
