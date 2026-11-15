using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStoryBuildersV2.Shared.Knowledge.Persistence;

/// <summary>KGB's string key-value store. The browser host implements it over localStorage; it holds the asb2:* keys.</summary>
public interface IKeyValueStore
{
    Task<string?> GetAsync(string key);

    /// <exception cref="StorageQuotaExceededException">When the browser quota is exceeded.</exception>
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
    Task<IReadOnlyList<string>> KeysAsync(string prefix);
}

public static class KeyValueJson
{
    /// <summary>camelCase JSON for values stored in localStorage (KGB's options).</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>An in-memory <see cref="IKeyValueStore"/> for tests; it can simulate a quota (KGB).</summary>
public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

    /// <summary>Total UTF-16 bytes allowed, or null for unlimited.</summary>
    public long? QuotaBytes { get; set; }

    public List<string> WriteLog { get; } = [];

    public IReadOnlyDictionary<string, string> Items => _items;

    public Task<string?> GetAsync(string key) => Task.FromResult(_items.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value)
    {
        if (QuotaBytes is { } quota)
        {
            var current = _items.Where(kv => kv.Key != key).Sum(kv => (long)(kv.Key.Length + kv.Value.Length) * 2);
            var next = current + (long)(key.Length + value.Length) * 2;
            if (next > quota) throw new StorageQuotaExceededException(next);
        }
        _items[key] = value;
        WriteLog.Add("set " + key);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _items.Remove(key);
        WriteLog.Add("remove " + key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> KeysAsync(string prefix) =>
        Task.FromResult<IReadOnlyList<string>>(_items.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
}
