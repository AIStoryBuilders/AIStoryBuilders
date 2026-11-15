using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Stories;
using Blazored.LocalStorage;
using Microsoft.JSInterop;

namespace AIStoryBuildersV2.Web.Services;

/// <summary>
/// <see cref="IKeyValueStore"/> over window.localStorage (KGB). Values are raw strings, not JSON-quoted (§5.5).
/// A quota error becomes <see cref="StorageQuotaExceededException"/> and is never swallowed (G5).
/// </summary>
public sealed class LocalStorageKeyValueStore(ILocalStorageService storage) : IKeyValueStore
{
    public async Task<string?> GetAsync(string key) => await storage.GetItemAsStringAsync(key);

    public async Task SetAsync(string key, string value)
    {
        try
        {
            await storage.SetItemAsStringAsync(key, value);
        }
        catch (JSException ex) when (ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageQuotaExceededException(await EstimateAsync() + (long)(key.Length + value.Length) * 2);
        }
    }

    public async Task RemoveAsync(string key) => await storage.RemoveItemAsync(key);

    public async Task<IReadOnlyList<string>> KeysAsync(string prefix)
    {
        var keys = await storage.KeysAsync();
        return keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    async Task<long> EstimateAsync()
    {
        long total = 0;
        foreach (var key in await KeysAsync(StoryStorageConstants.KeyPrefix))
            total += (long)(key.Length + ((await GetAsync(key))?.Length ?? 0)) * 2;
        return total;
    }
}
