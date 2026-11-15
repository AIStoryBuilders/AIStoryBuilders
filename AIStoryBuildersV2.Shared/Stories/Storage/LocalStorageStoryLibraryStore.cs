using System.Text.Json;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;

namespace AIStoryBuildersV2.Shared.Stories.Storage;

public sealed record StoryIndex(int Version, List<StoryIndexEntry> Stories);

/// <summary>WASM story index: JSON <see cref="StoryIndex"/> under asb2:stories (plan §5.5, §6.2).</summary>
public sealed class LocalStorageStoryLibraryStore(IKeyValueStore store, IStoryIndexLock indexLock) : IStoryLibraryStore
{
    /// <summary>
    /// Edits to existing stories that didn't fit in browser storage (G5). This tab keeps showing them, and the next save
    /// of the story writes them, so freeing space and pressing Retry doesn't lose them. SavedAt stays the stored value,
    /// because it is how another tab's save is recognized (§5.5 step 6).
    /// </summary>
    readonly Dictionary<string, StoryIndexEntry> _unsaved = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<StoryIndexEntry>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var stories = (await ReadAsync()).Stories;
        return _unsaved.Count == 0
            ? stories
            : stories.Select(s => _unsaved.TryGetValue(s.Title, out var edit) ? edit with { Id = s.Id, Title = s.Title, SavedAt = s.SavedAt } : s).ToList();
    }

    public async Task UpsertAsync(StoryIndexEntry entry, CancellationToken ct = default)
    {
        await indexLock.RunAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var index = await ReadAsync();
            var at = index.Stories.FindIndex(s => StoryTitle.SameTitle(s.Title, entry.Title));
            if (at >= 0) index.Stories[at] = entry with { Id = index.Stories[at].Id };
            else index.Stories.Add(entry with { Id = (index.Stories.Count + 1).ToString() });
            try
            {
                await WriteAsync(index);
                _unsaved.Remove(entry.Title);
            }
            catch (StorageQuotaExceededException)
            {
                if (at >= 0) _unsaved[entry.Title] = entry;
                throw;
            }
        }, ct);
    }

    public async Task RemoveAsync(string title, CancellationToken ct = default)
    {
        await indexLock.RunAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var index = await ReadAsync();
            _unsaved.Remove(title);
            if (index.Stories.RemoveAll(s => StoryTitle.SameTitle(s.Title, title)) > 0) await WriteAsync(index);
        }, ct);
    }

    async Task<StoryIndex> ReadAsync()
    {
        var json = await store.GetAsync(StoryStorageConstants.StoriesIndexKey);
        if (string.IsNullOrWhiteSpace(json)) return new StoryIndex(1, []);
        try
        {
            var index = JsonSerializer.Deserialize<StoryIndex>(json, KeyValueJson.Options);
            return index is null ? new StoryIndex(1, []) : index with { Stories = index.Stories ?? [] };
        }
        catch (JsonException)
        {
            return new StoryIndex(1, []);
        }
    }

    Task WriteAsync(StoryIndex index) =>
        store.SetAsync(StoryStorageConstants.StoriesIndexKey, JsonSerializer.Serialize(index, KeyValueJson.Options));
}
