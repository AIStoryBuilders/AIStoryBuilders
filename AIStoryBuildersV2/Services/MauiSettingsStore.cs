using System.Text.Json;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Settings;

namespace AIStoryBuildersV2.Services;

/// <summary>
/// KGB's MAUI store with asb keys (plan §6.3): provider, model, endpoint and verified models in Preferences; the API key in
/// SecureStorage (DPAPI on Windows). V1's AIStoryBuildersSettings.config is never read or written (D7).
/// </summary>
public sealed class MauiSettingsStore : ISettingsStore
{
    const string ProviderKey = "asb.llm.provider";
    const string ModelKey = "asb.llm.model";
    const string EndpointKey = "asb.llm.endpoint";
    const string VerifiedModelsKey = "asb.llm.verifiedModels";
    const string ApiKeyKey = "asb.llm.apiKey";

    public async Task<StoredSettings> LoadAsync()
    {
        var defaults = StoredSettings.Default;
        string? key = null;
        try { key = await SecureStorage.Default.GetAsync(ApiKeyKey); }
        catch (Exception) { /* Secure storage can be unavailable; treat as no key. */ }

        var endpoint = Preferences.Default.Get<string?>(EndpointKey, null);
        return new StoredSettings(
            Preferences.Default.Get(ProviderKey, defaults.Provider),
            Preferences.Default.Get(ModelKey, defaults.Model),
            string.IsNullOrEmpty(endpoint) ? null : endpoint,
            string.IsNullOrEmpty(key) ? null : key,
            ReadVerifiedModels());
    }

    static List<VerifiedModel> ReadVerifiedModels()
    {
        var json = Preferences.Default.Get<string?>(VerifiedModelsKey, null);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<VerifiedModel>>(json, KeyValueJson.Options) ?? []; }
        catch (JsonException) { return []; }
    }

    public async Task SaveAsync(StoredSettings settings)
    {
        Preferences.Default.Set(ProviderKey, settings.Provider);
        Preferences.Default.Set(ModelKey, settings.Model);
        Preferences.Default.Set(EndpointKey, settings.Endpoint ?? "");
        Preferences.Default.Set(VerifiedModelsKey, JsonSerializer.Serialize(settings.VerifiedModels ?? [], KeyValueJson.Options));
        if (string.IsNullOrEmpty(settings.ApiKey)) SecureStorage.Default.Remove(ApiKeyKey);
        else await SecureStorage.Default.SetAsync(ApiKeyKey, settings.ApiKey);
    }

    public Task ClearApiKeyAsync()
    {
        SecureStorage.Default.Remove(ApiKeyKey);
        return Task.CompletedTask;
    }
}
