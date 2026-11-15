using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Llm;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.Settings;

/// <summary>
/// KGB's settings service (D7) with the capability gate (D9, plan §5.9): a model is saved or used only after it passes
/// <see cref="IModelCapabilityProbe"/> for the current provider and endpoint host. Every AI call gets its client here (D10),
/// wrapped in <see cref="TemperatureFallbackChatClient"/>.
/// </summary>
public sealed class AppSettingsService(ISettingsStore store, IHostCapabilities host, HttpClient http, IModelCapabilityProbe probe)
    : IAppSettingsService, IChatClientProvider
{
    public const string NoKeyReason = "Add an API key in Settings";

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);
    StoredSettings? _cached;
    static readonly TimeSpan ModelListLifetime = TimeSpan.FromHours(24);
    readonly ConcurrentDictionary<string, (DateTimeOffset Stored, ModelListResult Result)> _models = new();

    public event Action? Changed;

    public static string NotVerifiedReason(string model) => $"Model '{model}' has not passed the capability check. Verify it in Settings.";

    async Task<StoredSettings> LoadAsync()
    {
        if (_cached is not null) return _cached;
        await _gate.WaitAsync();
        try { return _cached ??= await store.LoadAsync(); }
        finally { _gate.Release(); }
    }

    /// <summary>Verified models for this provider and host with the current probe version.</summary>
    static List<VerifiedModel> Matching(IEnumerable<VerifiedModel>? models, LlmProvider provider, string? endpoint)
    {
        var hostName = LlmDefaults.HostFor(provider, endpoint);
        return (models ?? []).Where(m => m.Provider == provider && m.ProbeVersion == ProbeConstants.Version
                                         && string.Equals(m.Host, hostName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    static bool IsVerified(StoredSettings s, LlmProvider provider, string? endpoint, string model) =>
        provider == LlmProvider.Fake || Matching(s.VerifiedModels, provider, endpoint).Any(m => string.Equals(m.Model, model, StringComparison.Ordinal));

    public async Task<AppSettings> GetAsync()
    {
        var s = await LoadAsync();
        var provider = s.ProviderValue;
        var verified = Matching(s.VerifiedModels, provider, s.Endpoint);
        // DEBUG's Fake provider needs no probe; list its model so the model dropdowns work.
        if (provider == LlmProvider.Fake && verified.Count == 0)
            verified.Add(new VerifiedModel(provider, null, string.IsNullOrEmpty(s.Model) ? "fake" : s.Model, ProbeConstants.Version, DateTimeOffset.UtcNow));
        return new AppSettings(host.RootDisplay, provider, s.Model, s.Endpoint, !string.IsNullOrWhiteSpace(s.ApiKey), verified);
    }

    public async Task<SettingsSaveResult> SaveAsync(LlmProvider provider, string model, string? endpoint, string? newApiKey, CancellationToken ct)
    {
        model = (model ?? "").Trim();
        endpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
        if (model.Length == 0 && provider != LlmProvider.Fake) return new SettingsSaveResult(false, null, "Pick or type a model name.");
        if (LlmDefaults.RequiresEndpoint(provider) && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            return new SettingsSaveResult(false, null, "Enter a full URL that starts with https:// or http://.");
        if (!LlmDefaults.RequiresEndpoint(provider)) endpoint = null;

        var current = await LoadAsync();
        var key = string.IsNullOrWhiteSpace(newApiKey) ? current.ApiKey : newApiKey.Trim();
        if (LlmDefaults.RequiresKey(provider) && string.IsNullOrWhiteSpace(key)) return new SettingsSaveResult(false, null, "Enter an API key.");

        // A changed provider or host keeps only the entries that match it; a changed key keeps the list (plan §5.9).
        var verified = Matching(current.VerifiedModels, provider, endpoint);
        ModelProbeResult? result = null;
        if (!IsVerified(current with { VerifiedModels = verified }, provider, endpoint, model))
        {
            result = await probe.ProbeAsync(new LlmOptions(provider, model, endpoint, key), ct);
            if (!result.Passed) return new SettingsSaveResult(false, result, null);
            verified.Add(new VerifiedModel(provider, LlmDefaults.HostFor(provider, endpoint), model, ProbeConstants.Version, DateTimeOffset.UtcNow));
        }

        var next = new StoredSettings(provider.ToString(), model, endpoint, key, verified);
        await store.SaveAsync(next);
        Update(next);
        return new SettingsSaveResult(true, result, null);
    }

    public async Task<ModelProbeResult> VerifyModelAsync(string model, CancellationToken ct)
    {
        model = (model ?? "").Trim();
        var s = await LoadAsync();
        var provider = s.ProviderValue;
        var result = await probe.ProbeAsync(new LlmOptions(provider, model, s.Endpoint, s.ApiKey), ct);
        if (!result.Passed) return result;

        var verified = Matching(s.VerifiedModels, provider, s.Endpoint).Where(m => m.Model != model).ToList();
        verified.Add(new VerifiedModel(provider, LlmDefaults.HostFor(provider, s.Endpoint), model, ProbeConstants.Version, DateTimeOffset.UtcNow));
        var next = s with { VerifiedModels = verified };
        await store.SaveAsync(next);
        Update(next);
        return result;
    }

    public async Task RemoveVerifiedModelAsync(string model)
    {
        var s = await LoadAsync();
        var next = s with { VerifiedModels = (s.VerifiedModels ?? []).Where(m => m.Model != model).ToList() };
        await store.SaveAsync(next);
        Update(next);
    }

    public async Task ClearApiKeyAsync()
    {
        await store.ClearApiKeyAsync();
        var current = await LoadAsync();
        Update(current with { ApiKey = null });
    }

    /// <summary>Called when another tab changed the settings key.</summary>
    public async Task ReloadAsync() => Update(await store.LoadAsync());

    void Update(StoredSettings next)
    {
        _cached = next;
        foreach (var client in _clients.Values) client.Dispose();
        _clients.Clear();
        Changed?.Invoke();
    }

    public async Task<ChatClientResult> GetClientAsync(string? model = null)
    {
        var s = await LoadAsync();
        var provider = s.ProviderValue;
        var m = string.IsNullOrWhiteSpace(model) ? s.Model : model.Trim();
        if (LlmDefaults.RequiresKey(provider) && string.IsNullOrWhiteSpace(s.ApiKey)) return new ChatClientResult(null, NoKeyReason);
        if (!IsVerified(s, provider, s.Endpoint, m)) return new ChatClientResult(null, NotVerifiedReason(m));

        if (_clients.TryGetValue(m, out var cached)) return new ChatClientResult(cached, null);
        try
        {
            var client = new TemperatureFallbackChatClient(ChatClientFactory.Create(new LlmOptions(provider, m, s.Endpoint, s.ApiKey), http, host.IsBrowser));
            return new ChatClientResult(_clients.GetOrAdd(m, client), null);
        }
        catch (ArgumentException ex)
        {
            return new ChatClientResult(null, ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase) ? NoKeyReason : ex.Message);
        }
    }

    public async Task<string?> TestConnectionAsync(CancellationToken ct)
    {
        var result = await GetClientAsync();
        if (!result.Ok) return result.Reason;
        try
        {
            await result.Client.GetResponseAsync([new ChatMessage(ChatRole.User, ProbeFixtures.ConnectMessage)],
                new ChatOptions { MaxOutputTokens = ProbeConstants.MaxOutputTokens }, ct);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "The request timed out.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var s = await LoadAsync();
            return DescribeError(ex, LlmDefaults.HostFor(s.ProviderValue, s.Endpoint), host.IsBrowser);
        }
    }

    public async Task<ModelListResult> ListModelsAsync(LlmProvider provider, string? endpoint, string? newApiKey, bool refresh, CancellationToken ct)
    {
        var s = await LoadAsync();
        var key = string.IsNullOrWhiteSpace(newApiKey) ? s.ApiKey : newApiKey.Trim();
        // A saved key goes only to the host it was saved for, so a changed endpoint needs the key typed again.
        if (string.IsNullOrWhiteSpace(newApiKey) && LlmDefaults.RequiresKey(provider) && !string.IsNullOrWhiteSpace(key) &&
            (provider != s.ProviderValue || LlmDefaults.HostFor(provider, endpoint) != LlmDefaults.HostFor(s.ProviderValue, s.Endpoint)))
            return ModelListResult.NotConfigured("Enter the API key for this provider to list its models.");

        var options = new LlmOptions(provider, "", endpoint, key);
        var cacheKey = ModelCacheKey(options);
        if (!refresh && _models.TryGetValue(cacheKey, out var hit) && DateTimeOffset.UtcNow - hit.Stored < ModelListLifetime)
            return hit.Result;

        var result = await ModelCatalog.ListAsync(options, http, host.IsBrowser, ct);
        if (result.Status == ModelListStatus.Success) _models[cacheKey] = (DateTimeOffset.UtcNow, result);
        else _models.TryRemove(cacheKey, out _);
        return result;
    }

    /// <summary>Provider, endpoint host and a short hash of the key, so the cache never holds the key itself.</summary>
    internal static string ModelCacheKey(LlmOptions options)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.ApiKey?.Trim() ?? "")))[..16];
        return $"{options.Provider}|{options.Endpoint?.Trim().TrimEnd('/').ToLowerInvariant()}|{hash}";
    }

    /// <summary>A user-facing message for a provider failure. Never contains the API key (KGB).</summary>
    public static string DescribeError(Exception ex, string? providerHost, bool isBrowser)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return "The provider rejected the API key (" + (int)httpEx.StatusCode + "). Check the key in Settings.";
            if (httpEx.StatusCode is null && isBrowser)
                return $"The browser blocked the request to {providerHost ?? "the provider"}. See Settings help.";
            return Redact(httpEx.Message);
        }
        if (ex is System.ClientModel.ClientResultException cre)
        {
            if (cre.Status is 401 or 403) return $"The provider rejected the API key ({cre.Status}). Check the key in Settings.";
            if (cre.Status == 0 && isBrowser) return $"The browser blocked the request to {providerHost ?? "the provider"}. See Settings help.";
            return Redact(cre.Message);
        }
        if (ex is JsonException) return "The provider returned an unexpected response.";
        return Redact(ex.Message);
    }

    static string Redact(string message)
    {
        // Defensive: strip anything that looks like a key.
        message = System.Text.RegularExpressions.Regex.Replace(message, @"(sk-[A-Za-z0-9_\-]{8,}|AIza[A-Za-z0-9_\-]{20,}|[A-Fa-f0-9]{32,})", "[redacted]");
        return message.Length > 400 ? message[..400] + "…" : message;
    }
}

/// <summary>WASM settings: one localStorage key, asb2:settings, in plaintext (D7, §9).</summary>
public sealed class BrowserSettingsStore(IKeyValueStore kv) : ISettingsStore
{
    public async Task<StoredSettings> LoadAsync()
    {
        var json = await kv.GetAsync(StoryStorageConstants.SettingsKey);
        if (string.IsNullOrWhiteSpace(json)) return StoredSettings.Default;
        try
        {
            var s = JsonSerializer.Deserialize<StoredSettings>(json, KeyValueJson.Options);
            return s is null || string.IsNullOrWhiteSpace(s.Provider) ? StoredSettings.Default : s with { Model = s.Model ?? "" };
        }
        catch (JsonException) { return StoredSettings.Default; }
    }

    public Task SaveAsync(StoredSettings settings) =>
        kv.SetAsync(StoryStorageConstants.SettingsKey, JsonSerializer.Serialize(settings, KeyValueJson.Options));

    public async Task ClearApiKeyAsync()
    {
        var current = await LoadAsync();
        await SaveAsync(current with { ApiKey = null });
    }
}
