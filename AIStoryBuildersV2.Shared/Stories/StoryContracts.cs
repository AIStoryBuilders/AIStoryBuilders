namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>One row of the story library: a line of AIStoryBuildersStories.csv (MAUI) or an entry of asb2:stories (WASM).</summary>
public sealed record StoryIndexEntry(string Id, string Title, string Style, string Theme,
                                     string Synopsis, string WorldFacts, DateTimeOffset? SavedAt);

/// <summary>The story index, which lives outside the story folders (D5).</summary>
public interface IStoryLibraryStore
{
    Task<IReadOnlyList<StoryIndexEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>Adds or replaces the entry with the same title (ordinal, ignoring case).</summary>
    Task UpsertAsync(StoryIndexEntry entry, CancellationToken ct = default);
    Task RemoveAsync(string title, CancellationToken ct = default);
}

public enum StoryPersistenceEventKind { Saved, SaveFailed, ChangedElsewhere }

public sealed record StoryPersistenceEvent(StoryPersistenceEventKind Kind, string Title, string? Message = null);

public sealed record StorageUsage(long EstimatedBytes, long AssumedQuotaBytes, int Stories);

/// <summary>Durable storage of a story folder. MAUI: the files are the store. WASM: a zip per story in localStorage (§5.5).</summary>
public interface IStoryPersistence
{
    /// <summary>WASM: extract the zip into MEMFS. MAUI: no-op.</summary>
    Task OpenAsync(string title, CancellationToken ct = default);

    /// <summary>WASM: start the debounced save. MAUI: no-op.</summary>
    void MarkDirty(string title);

    /// <param name="title">null saves every dirty story.</param>
    Task FlushAsync(string? title = null, CancellationToken ct = default);
    Task DeleteAsync(string title, CancellationToken ct = default);

    /// <summary>WASM: drop the MEMFS copy and the dirty flag, then extract the zip again. MAUI: no-op.</summary>
    Task ReloadAsync(string title, CancellationToken ct = default);
    bool IsDirty(string title);

    /// <returns>null on MAUI.</returns>
    Task<StorageUsage?> GetUsageAsync(CancellationToken ct = default);
    event Action<StoryPersistenceEvent>? Changed;
}

public static class StoryStorageConstants
{
    public const string KeyPrefix = "asb2:";
    public const string StoriesIndexKey = KeyPrefix + "stories";
    public static string ZipKey(string title) => KeyPrefix + "story:" + title;
    public const string LogKey = KeyPrefix + "log";
    public const string SettingsKey = KeyPrefix + "settings";
    public const string BrowserGuidKey = KeyPrefix + "guid";
    public const int SaveDebounceMs = 1500;
    public const long AssumedQuotaBytes = 5_000_000;
    public const double WarnAtFraction = 0.8;
    public const long MaxStybldImportBytes = 50L * 1024 * 1024;
    public const long MaxManuscriptImportBytes = 50L * 1024 * 1024;

    /// <summary>The fixed format version written to .stybld manifests (D6). It is V1's version.</summary>
    public const string StybldFormatVersion = "03.62.00";
    public const string KnowledgeBaseFolderName = "KnowledgeBase";
}
