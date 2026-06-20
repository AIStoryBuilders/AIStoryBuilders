using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using OpenAI;
using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mscc.GenerativeAI;

namespace AIStoryBuilders.AI;

/// <summary>
/// Fetches, caches, and returns available AI model lists from each provider.
/// Registered as a singleton in DI.
/// </summary>
public class AIModelService
{
    private readonly LogService _logService;
    private readonly string _cacheFolder;
    private readonly TimeSpan _cacheTtl;

    public AIModelService(LogService logService)
    {
        _logService = logService;
        _cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AIStoryBuilders", "ModelCache");
        _cacheTtl = TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Returns models from cache if fresh, otherwise fetches from the API.
    /// Falls back to defaults on failure.
    /// </summary>
    public async Task<List<string>> GetModelsAsync(
        string serviceType,
        string apiKey,
        string endpoint = null,
        string apiVersion = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new List<string>();

        try
        {
            // Try cache first
            var cached = GetCachedModels(serviceType, apiKey);
            if (cached != null)
                return cached;

            // Cache miss or expired — fetch from API
            var models = await FetchModelsFromApiAsync(serviceType, apiKey, endpoint, apiVersion);
            CacheModels(serviceType, apiKey, models);
            return models;
        }
        catch (Exception ex)
        {
            _logService.WriteToLog($"AIModelService.GetModelsAsync failed for {serviceType}: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Forces a fresh fetch from the provider API, ignoring cache.
    /// Falls back to defaults on failure.
    /// </summary>
    public async Task<List<string>> RefreshModelsAsync(
        string serviceType,
        string apiKey,
        string endpoint = null,
        string apiVersion = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new List<string>();

        try
        {
            var models = await FetchModelsFromApiAsync(serviceType, apiKey, endpoint, apiVersion);
            CacheModels(serviceType, apiKey, models);
            return models;
        }
        catch (Exception ex)
        {
            _logService.WriteToLog($"AIModelService.RefreshModelsAsync failed for {serviceType}: {ex.Message}");
            return new List<string>();
        }
    }

    // ──────────────────────────────────────────
    //  Private — per-provider fetch
    // ──────────────────────────────────────────

    private async Task<List<string>> FetchModelsFromApiAsync(
        string serviceType, string apiKey, string endpoint, string apiVersion)
    {
        return serviceType switch
        {
            "OpenAI" => await FetchOpenAIModelsAsync(apiKey),
            "Azure OpenAI" => await FetchAzureOpenAIModelsAsync(apiKey, endpoint, apiVersion),
            "Anthropic" => await FetchAnthropicModelsAsync(apiKey),
            "Google AI" => await FetchGoogleAIModelsAsync(apiKey),
            _ => new List<string>()
        };
    }

    private async Task<List<string>> FetchOpenAIModelsAsync(string apiKey)
    {
        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        var modelClient = client.GetOpenAIModelClient();
        var response = await modelClient.GetModelsAsync();

        var models = response.Value
            .Where(m => m.Id.StartsWith("gpt-") ||
                        m.Id.StartsWith("o1") ||
                        m.Id.StartsWith("o3") ||
                        m.Id.StartsWith("o4"))
            .Where(m => !m.Id.Contains("instruct") &&
                        !m.Id.Contains("realtime") &&
                        !m.Id.Contains("audio"))
            .Select(m => m.Id)
            .Distinct()
            .OrderByDescending(m => m)
            .ToList();

        return models;
    }

    private async Task<List<string>> FetchAzureOpenAIModelsAsync(
        string apiKey, string endpoint, string apiVersion)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return new List<string>();

        if (string.IsNullOrWhiteSpace(apiVersion))
            apiVersion = "2024-10-21";

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("api-key", apiKey);

        // Try /openai/models first
        var modelsUrl = $"{endpoint.TrimEnd('/')}/openai/models?api-version={apiVersion}";
        try
        {
            var response = await http.GetAsync(modelsUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var models = ParseAzureModelList(json);
                if (models.Count > 0)
                    return models;
            }
        }
        catch
        {
            // Fall through to deployments endpoint
        }

        // Fallback: /openai/deployments
        var deploymentsUrl = $"{endpoint.TrimEnd('/')}/openai/deployments?api-version={apiVersion}";
        try
        {
            var response = await http.GetAsync(deploymentsUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var models = ParseAzureModelList(json);
                if (models.Count > 0)
                    return models;
            }
        }
        catch
        {
            // Fall through to empty list
        }

        return new List<string>();
    }

    private static List<string> ParseAzureModelList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var dataArray))
            return new List<string>();

        var models = new List<string>();
        foreach (var item in dataArray.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idProp))
            {
                var id = idProp.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    models.Add(id);
            }
        }

        return models.Distinct().OrderByDescending(m => m).ToList();
    }

    private async Task<List<string>> FetchAnthropicModelsAsync(string apiKey)
    {
        try
        {
            var models = new List<string>();
            var url = "https://api.anthropic.com/v1/models?limit=1000";

            using var http = new HttpClient();

            // Page through results in case there are more than the page size.
            while (!string.IsNullOrEmpty(url))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                request.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");

                var response = await http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return models;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var dataArray) &&
                    dataArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idProp))
                        {
                            var id = idProp.GetString();
                            if (!string.IsNullOrWhiteSpace(id))
                                models.Add(id);
                        }
                    }
                }

                var hasMore = root.TryGetProperty("has_more", out var hasMoreProp) &&
                              hasMoreProp.ValueKind == JsonValueKind.True;
                var lastId = root.TryGetProperty("last_id", out var lastIdProp)
                    ? lastIdProp.GetString()
                    : null;

                url = hasMore && !string.IsNullOrEmpty(lastId)
                    ? $"https://api.anthropic.com/v1/models?limit=1000&after_id={Uri.EscapeDataString(lastId)}"
                    : null;
            }

            models = models.Distinct().OrderByDescending(m => m, StringComparer.Ordinal).ToList();
            return models;
        }
        catch (Exception ex)
        {
            _logService.WriteToLog($"AIModelService.FetchAnthropicModelsAsync failed: {ex.Message}");
            return new List<string>();
        }
    }

    private async Task<List<string>> FetchGoogleAIModelsAsync(string apiKey)
    {
        var googleAI = new GoogleAI(apiKey: apiKey);
        var generativeModel = googleAI.GenerativeModel(model: "gemini-1.5-pro");
        var response = await generativeModel.ListModels();

        var models = response
            .Where(m => m.Name != null && m.Name.Contains("gemini", StringComparison.OrdinalIgnoreCase))
            .Where(m => m.SupportedGenerationMethods != null &&
                        m.SupportedGenerationMethods.Contains("generateContent"))
            .Where(m => !m.Name.Contains("embedding", StringComparison.OrdinalIgnoreCase) &&
                        !m.Name.Contains("aqa", StringComparison.OrdinalIgnoreCase) &&
                        !m.Name.Contains("imagen", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? m.Name.Substring("models/".Length)
                : m.Name)
            .Distinct()
            .OrderByDescending(m => m)
            .ToList();

        return models;
    }

    // ──────────────────────────────────────────
    //  Private — local file-system cache
    // ──────────────────────────────────────────

    private List<string> GetCachedModels(string serviceType, string apiKey)
    {
        try
        {
            var filePath = GetCacheFilePath(serviceType, apiKey);
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            var entry = JsonSerializer.Deserialize<ModelCacheEntry>(json);

            if (entry == null || entry.Models == null || entry.Models.Count == 0)
                return null;

            if (DateTimeOffset.UtcNow - entry.LastFetched > _cacheTtl)
                return null; // expired

            return entry.Models;
        }
        catch (Exception ex)
        {
            _logService.WriteToLog($"AIModelService cache read failed: {ex.Message}");
            // Delete corrupted cache file
            try
            {
                var filePath = GetCacheFilePath(serviceType, apiKey);
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch { /* best effort */ }
            return null;
        }
    }

    private void CacheModels(string serviceType, string apiKey, List<string> models)
    {
        try
        {
            Directory.CreateDirectory(_cacheFolder);

            var entry = new ModelCacheEntry
            {
                ServiceType = serviceType,
                Models = models,
                LastFetched = DateTimeOffset.UtcNow
            };

            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            var filePath = GetCacheFilePath(serviceType, apiKey);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            _logService.WriteToLog($"AIModelService cache write failed: {ex.Message}");
        }
    }

    private string GetCacheFilePath(string serviceType, string apiKey)
    {
        var sanitizedType = serviceType.ToLowerInvariant().Replace(" ", "-");
        var cacheKey = GetCacheKey(apiKey);
        return Path.Combine(_cacheFolder, $"{sanitizedType}_{cacheKey}.json");
    }

    private static string GetCacheKey(string apiKey)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(apiKey.Trim()));
        return Convert.ToHexString(hash)[..16];
    }
}
