namespace AIStoryBuildersV2.Shared.Stories.Storage;

/// <summary>MAUI persistence: the files under Documents\AIStoryBuilders are the store, so every call is a no-op.</summary>
public sealed class DiskStoryPersistence : IStoryPersistence
{
    public Task OpenAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public void MarkDirty(string title) { }
    public Task FlushAsync(string? title = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public Task ReloadAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsDirty(string title) => false;
    public Task<StorageUsage?> GetUsageAsync(CancellationToken ct = default) => Task.FromResult<StorageUsage?>(null);

    public event Action<StoryPersistenceEvent>? Changed { add { } remove { } }
}
