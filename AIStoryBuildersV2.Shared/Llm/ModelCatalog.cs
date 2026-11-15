using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIStoryBuildersV2.Shared.Llm;

public enum ModelListStatus { Success, InvalidKey, Unreachable, Empty, NotConfigured }

/// <summary>The models a provider offers. There is no built-in fallback list: a failure is shown and the user can type a name.</summary>
public sealed record ModelListResult(IReadOnlyList<string> Models, ModelListStatus Status, string? Message)
{
    public static ModelListResult Success(IReadOnlyList<string> models) =>
        models.Count == 0 ? Empty() : new(models, ModelListStatus.Success, null);

    public static ModelListResult Empty() => new([], ModelListStatus.Empty, "The provider returned no models.");
    public static ModelListResult InvalidKey() => new([], ModelListStatus.InvalidKey, "The provider rejected the API key.");
    public static ModelListResult NotConfigured(string message) => new([], ModelListStatus.NotConfigured, message);
    public static ModelListResult Unreachable(string detail) => new([], ModelListStatus.Unreachable, detail);
}

/// <summary>
/// Lists models from each provider's own API, so the Settings model box can offer a dropdown. Adapted from the
/// AspireDeployService model catalogs: Anthropic /v1/models (paged), OpenAI /v1/models (chat models only),
/// Azure OpenAI deployments, and Ollama /api/tags. Works in the browser too; failures there are usually CORS.
/// </summary>
public static class ModelCatalog
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    const int MaxAnthropicPages = 10;
    const string AzureDeploymentsApiVersion = "2023-03-15-preview";
    const string AzureModelsApiVersion = "2024-06-01";

    public static async Task<ModelListResult> ListAsync(LlmOptions options, HttpClient http, bool isBrowser, CancellationToken ct)
    {
        var key = options.ApiKey?.Trim() ?? "";
        if (LlmDefaults.RequiresKey(options.Provider) && key.Length == 0)
            return ModelListResult.NotConfigured("Add an API key to list models.");
        Uri? endpoint = null;
        if (LlmDefaults.RequiresEndpoint(options.Provider) &&
            (!Uri.TryCreate(options.Endpoint?.Trim(), UriKind.Absolute, out endpoint) || endpoint.Scheme is not ("http" or "https")))
            return ModelListResult.NotConfigured("Enter the endpoint to list models.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            return options.Provider switch
            {
                LlmProvider.Anthropic => await ListAnthropicAsync(http, key, isBrowser, cts.Token),
                LlmProvider.OpenAI => await ListOpenAIAsync(http, key, cts.Token),
                LlmProvider.AzureOpenAI => await ListAzureAsync(http, endpoint!, key, cts.Token),
                LlmProvider.GoogleAI => await ListGoogleAsync(http, key, cts.Token),
                LlmProvider.Ollama => await ListOllamaAsync(http, endpoint!, cts.Token),
                LlmProvider.Fake => ModelListResult.Success(["fake"]),
                _ => ModelListResult.NotConfigured("This provider has no model list. Type the model name."),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ModelListResult.Unreachable("The model list request timed out.");
        }
        catch (HttpRequestException ex)
        {
            var host = LlmDefaults.HostFor(options.Provider, options.Endpoint) ?? "the provider";
            return ModelListResult.Unreachable(isBrowser && ex.StatusCode is null
                ? $"The browser blocked the request to {host}, or it couldn't be reached."
                : $"Couldn't reach {host}.");
        }
        catch (JsonException)
        {
            return ModelListResult.Unreachable("The provider returned an unexpected model list.");
        }
    }

    static async Task<ModelListResult> ListAnthropicAsync(HttpClient http, string key, bool isBrowser, CancellationToken ct)
    {
        var models = new List<string>();
        string? afterId = null;
        for (var page = 0; page < MaxAnthropicPages; page++)
        {
            var url = "https://api.anthropic.com/v1/models?limit=100" + (afterId is null ? "" : "&after_id=" + Uri.EscapeDataString(afterId));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", key);
            request.Headers.Add("anthropic-version", AnthropicChatClient.ApiVersion);
            if (isBrowser) request.Headers.Add("anthropic-dangerous-direct-browser-access", "true");

            var (failure, body) = await SendAsync(http, request, ct);
            if (failure is not null) return failure;
            var (ids, hasMore, lastId) = ParseAnthropicPage(body!);
            models.AddRange(ids);
            if (!hasMore || string.IsNullOrEmpty(lastId)) break;
            afterId = lastId;
        }
        // The API lists newest first; keep that order.
        return ModelListResult.Success(models.Distinct(StringComparer.Ordinal).ToList());
    }

    static async Task<ModelListResult> ListOpenAIAsync(HttpClient http, string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var (failure, body) = await SendAsync(http, request, ct);
        return failure ?? ModelListResult.Success(FilterOpenAIChatModels(ParseIds(body!)));
    }

    /// <summary>Deployment names, not base model ids, are what the chat client needs. Older api-versions serve the deployments route.</summary>
    static async Task<ModelListResult> ListAzureAsync(HttpClient http, Uri endpoint, string key, CancellationToken ct)
    {
        var root = endpoint.GetLeftPart(UriPartial.Authority);
        string[] urls =
        [
            $"{root}/openai/deployments?api-version={AzureDeploymentsApiVersion}",
            $"{root}/openai/models?api-version={AzureModelsApiVersion}",
        ];
        ModelListResult? last = null;
        foreach (var url in urls)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("api-key", key);
            var (failure, body) = await SendAsync(http, request, ct);
            if (failure is { Status: ModelListStatus.InvalidKey }) return failure;
            if (failure is not null) { last = failure; continue; }
            var names = ParseIds(body!, alsoName: true).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count > 0) return ModelListResult.Success(names);
            last = ModelListResult.Empty();
        }
        return last ?? ModelListResult.Empty();
    }

    /// <summary>Gemini models that support generateContent (D8), paged; "models/" is stripped from the names.</summary>
    static async Task<ModelListResult> ListGoogleAsync(HttpClient http, string key, CancellationToken ct)
    {
        var models = new List<string>();
        string? pageToken = null;
        for (var page = 0; page < 10; page++)
        {
            var url = GoogleAIChatClient.BaseUrl + "/models?pageSize=1000" + (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-goog-api-key", key);
            var (failure, body) = await SendAsync(http, request, ct);
            if (failure is { Status: ModelListStatus.Unreachable } && failure.Message?.Contains("HTTP 400") == true) return ModelListResult.InvalidKey();
            if (failure is not null) return failure;
            var (names, next) = ParseGoogleModels(body!);
            models.AddRange(names);
            if (string.IsNullOrEmpty(next)) break;
            pageToken = next;
        }
        return ModelListResult.Success(models.Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static (List<string> Names, string? NextPageToken) ParseGoogleModels(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var names = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in list.EnumerateArray())
            {
                var supportsGenerate = m.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array
                    && methods.EnumerateArray().Any(x => x.GetString() == "generateContent");
                var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (supportsGenerate && !string.IsNullOrWhiteSpace(name)) names.Add(name.StartsWith("models/", StringComparison.Ordinal) ? name[7..] : name);
            }
        }
        var next = doc.RootElement.TryGetProperty("nextPageToken", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return (names, next);
    }

    static async Task<ModelListResult> ListOllamaAsync(HttpClient http, Uri endpoint, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "/api/tags"));
        var (failure, body) = await SendAsync(http, request, ct);
        return failure ?? ModelListResult.Success(ParseOllamaTags(body!));
    }

    static async Task<(ModelListResult? Failure, string? Body)> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return (ModelListResult.InvalidKey(), null);
        if (!response.IsSuccessStatusCode)
            return (ModelListResult.Unreachable($"The provider answered HTTP {(int)response.StatusCode}."), null);
        return (null, await response.Content.ReadAsStringAsync(ct));
    }

    internal static (List<string> Ids, bool HasMore, string? LastId) ParseAnthropicPage(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var ids = ReadIds(root, alsoName: false);
        var hasMore = root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
        var lastId = root.TryGetProperty("last_id", out var last) && last.ValueKind == JsonValueKind.String ? last.GetString() : null;
        return (ids, hasMore, lastId);
    }

    internal static List<string> ParseIds(string body, bool alsoName = false)
    {
        using var doc = JsonDocument.Parse(body);
        return ReadIds(doc.RootElement, alsoName);
    }

    internal static List<string> ParseOllamaTags(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var names = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in models.EnumerateArray())
            {
                var name = m.TryGetProperty("name", out var n) ? n.GetString() : m.TryGetProperty("model", out var mm) ? mm.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
        }
        return names.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Chat models only. If a new naming scheme filters everything out, all models are returned instead.</summary>
    internal static List<string> FilterOpenAIChatModels(IEnumerable<string> ids)
    {
        var all = ids.Distinct(StringComparer.Ordinal).OrderByDescending(m => m, StringComparer.Ordinal).ToList();
        var chat = all
            .Where(m => m.StartsWith("gpt-") || m.StartsWith("chatgpt-") || m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4"))
            .Where(m => !m.Contains("instruct") && !m.Contains("realtime") && !m.Contains("audio") && !m.Contains("transcribe") && !m.Contains("tts") && !m.Contains("image"))
            .ToList();
        return chat.Count > 0 ? chat : all;
    }

    static List<string> ReadIds(JsonElement root, bool alsoName)
    {
        var ids = new List<string>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var value = item.TryGetProperty("id", out var id) ? id.GetString() : null;
                if (value is null && alsoName && item.TryGetProperty("name", out var name)) value = name.GetString();
                if (!string.IsNullOrWhiteSpace(value)) ids.Add(value);
            }
        }
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }
}
