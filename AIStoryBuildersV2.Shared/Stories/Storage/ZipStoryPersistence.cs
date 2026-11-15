using System.IO.Compression;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;

namespace AIStoryBuildersV2.Shared.Stories.Storage;

/// <summary>Thrown when the story index lists a story whose zip is missing or unreadable (plan §7.1).</summary>
public sealed class StoryDataMissingException(string title, Exception? inner = null)
    : Exception("Story data is missing from browser storage", inner)
{
    public string Title { get; } = title;
}

/// <summary>
/// WASM persistence (D3, §5.5): each story is a base64 zip under asb2:story:{Title}. The open story is extracted into
/// MEMFS, saves are debounced after each mutation, and a full quota raises SaveFailed and keeps the story dirty.
/// Nothing is swallowed (G5).
/// </summary>
public sealed class ZipStoryPersistence : IStoryPersistence
{
    readonly IKeyValueStore _store;
    readonly IHostCapabilities _host;
    readonly IStoryLibraryStore _library;
    readonly Func<DateTimeOffset> _now;
    readonly SemaphoreSlim _saveLock = new(1, 1);
    readonly object _gate = new();
    readonly Dictionary<string, StoryState> _states = new(StringComparer.OrdinalIgnoreCase);
    string? _openTitle;

    sealed class StoryState
    {
        public bool Dirty;
        public int Version;
        public CancellationTokenSource? Debounce;
        public DateTimeOffset? LoadedSavedAt;
        public DateTimeOffset? LastOwnSavedAt;
    }

    public ZipStoryPersistence(IKeyValueStore store, IHostCapabilities host, IStoryLibraryStore library, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _host = host;
        _library = library;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The debounce after a mutation (1500 ms). Tests shorten it.</summary>
    public TimeSpan SaveDelay { get; set; } = TimeSpan.FromMilliseconds(StoryStorageConstants.SaveDebounceMs);

    public event Action<StoryPersistenceEvent>? Changed;

    /// <summary>The story currently extracted in MEMFS, if any.</summary>
    public string? OpenTitle => _openTitle;

    string Folder(string title) => Path.Combine(_host.RootPath, title);

    StoryState State(string title)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(title, out var state)) _states[title] = state = new StoryState();
            return state;
        }
    }

    public bool IsDirty(string title)
    {
        lock (_gate) return _states.TryGetValue(title, out var s) && s.Dirty;
    }

    /// <summary>
    /// Extracts the story into MEMFS. A dirty story keeps its MEMFS copy (the unsaved copy left by a failed save).
    /// Opening another story first flushes the current one; if that fails, the open is blocked.
    /// </summary>
    /// <exception cref="StorageQuotaExceededException">The previous story could not be saved.</exception>
    /// <exception cref="StoryDataMissingException">The zip is missing or corrupt; MEMFS is left untouched.</exception>
    public async Task OpenAsync(string title, CancellationToken ct = default)
    {
        if (_openTitle is { } previous && !StoryTitle.SameTitle(previous, title))
        {
            await FlushAsync(previous, ct);
            if (Directory.Exists(Folder(previous))) Directory.Delete(Folder(previous), recursive: true);
            _openTitle = null;
        }

        var state = State(title);
        if (!(IsDirty(title) && Directory.Exists(Folder(title))))
        {
            await ExtractAsync(title);
        }

        state.LoadedSavedAt = (await _library.ListAsync(ct)).FirstOrDefault(e => StoryTitle.SameTitle(e.Title, title))?.SavedAt;
        _openTitle = title;
    }

    async Task ExtractAsync(string title)
    {
        var base64 = await _store.GetAsync(StoryStorageConstants.ZipKey(title));
        if (string.IsNullOrEmpty(base64)) throw new StoryDataMissingException(title);

        List<(string Path, byte[] Bytes)> files;
        try
        {
            using var archive = new ZipArchive(new MemoryStream(Convert.FromBase64String(base64)), ZipArchiveMode.Read);
            var limits = ZipLimits.BrowserStory;
            long total = 0;
            files = SafeZip.ValidatedEntries(archive, limits).Select(e =>
            {
                using var source = e.Entry.Open();
                using var buffer = new MemoryStream();
                total += SafeZip.CopyBounded(source, buffer, limits.MaxEntryBytes, limits.MaxTotalBytes - total);
                return (e.Path, buffer.ToArray());
            }).ToList();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException)
        {
            throw new StoryDataMissingException(title, ex);
        }

        // Only now, with every entry read, replace the MEMFS copy.
        var folder = Folder(title);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        foreach (var (path, bytes) in files)
        {
            var full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, bytes);
        }
    }

    public void MarkDirty(string title)
    {
        CancellationTokenSource debounce;
        var state = State(title);
        lock (_gate)
        {
            state.Dirty = true;
            state.Version++;
            state.Debounce?.Cancel();
            state.Debounce = debounce = new CancellationTokenSource();
        }
        _ = SaveAfterDelayAsync(title, debounce.Token);
    }

    async Task SaveAfterDelayAsync(string title, CancellationToken token)
    {
        try
        {
            await Task.Delay(SaveDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await SaveAsync(title);
        }
        catch (Exception)
        {
            // SaveAsync already raised SaveFailed; the story stays dirty for Retry.
        }
    }

    public async Task FlushAsync(string? title = null, CancellationToken ct = default)
    {
        List<string> titles;
        lock (_gate)
        {
            titles = _states.Where(kv => kv.Value.Dirty && (title is null || StoryTitle.SameTitle(kv.Key, title)))
                .Select(kv => kv.Key).ToList();
            foreach (var t in titles) _states[t].Debounce?.Cancel();
        }
        foreach (var t in titles) await SaveAsync(t);
    }

    /// <summary>Zips the MEMFS folder, stores it and stamps the index entry.</summary>
    async Task SaveAsync(string title)
    {
        await _saveLock.WaitAsync();
        try
        {
            var state = State(title);
            int version;
            lock (_gate)
            {
                if (!state.Dirty) return;
                version = state.Version;
            }

            var folder = Folder(title);
            if (!Directory.Exists(folder))
            {
                lock (_gate) state.Dirty = false;
                return;
            }

            try
            {
                // Same layout as .stybld, minus Manifest.config (§5.5).
                var zip = SafeZip.ZipFolder(folder, path => path.Equals("Manifest.config", StringComparison.OrdinalIgnoreCase));
                await _store.SetAsync(StoryStorageConstants.ZipKey(title), Convert.ToBase64String(zip));

                var savedAt = _now();
                var entry = (await _library.ListAsync()).FirstOrDefault(e => StoryTitle.SameTitle(e.Title, title));
                if (entry is not null) await _library.UpsertAsync(entry with { SavedAt = savedAt });

                lock (_gate)
                {
                    state.LastOwnSavedAt = savedAt;
                    if (state.Version == version) state.Dirty = false;
                }
            }
            catch (Exception ex)
            {
                Changed?.Invoke(new StoryPersistenceEvent(StoryPersistenceEventKind.SaveFailed, title, ex.Message));
                throw;
            }

            Changed?.Invoke(new StoryPersistenceEvent(StoryPersistenceEventKind.Saved, title));
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task DeleteAsync(string title, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_states.Remove(title, out var state)) state.Debounce?.Cancel();
        }
        await _store.RemoveAsync(StoryStorageConstants.ZipKey(title));
        if (_openTitle is not null && StoryTitle.SameTitle(_openTitle, title)) _openTitle = null;
    }

    public async Task ReloadAsync(string title, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(title, out var state))
            {
                state.Debounce?.Cancel();
                state.Dirty = false;
            }
        }
        if (Directory.Exists(Folder(title))) Directory.Delete(Folder(title), recursive: true);
        await OpenAsync(title, ct);
    }

    public async Task<StorageUsage?> GetUsageAsync(CancellationToken ct = default)
    {
        long total = 0;
        var stories = 0;
        foreach (var key in await _store.KeysAsync(StoryStorageConstants.KeyPrefix))
        {
            total += (long)(key.Length + ((await _store.GetAsync(key))?.Length ?? 0)) * 2;
            if (key.StartsWith(StoryStorageConstants.KeyPrefix + "story:", StringComparison.Ordinal)) stories++;
        }
        return new StorageUsage(total, StoryStorageConstants.AssumedQuotaBytes, stories);
    }

    /// <summary>
    /// Called by the storage event bridge when another tab changes an asb2:* key (§5.5 step 6). Raises ChangedElsewhere
    /// when the open story's SavedAt differs from both the loaded value and this tab's last save.
    /// </summary>
    public async Task OnExternalChangeAsync(string? key)
    {
        if (_openTitle is not { } title) return;
        if (key is not null && key != StoryStorageConstants.StoriesIndexKey && key != StoryStorageConstants.ZipKey(title)) return;

        var savedAt = (await _library.ListAsync()).FirstOrDefault(e => StoryTitle.SameTitle(e.Title, title))?.SavedAt;
        var state = State(title);
        if (savedAt != state.LoadedSavedAt && savedAt != state.LastOwnSavedAt)
            Changed?.Invoke(new StoryPersistenceEvent(StoryPersistenceEventKind.ChangedElsewhere, title));
    }
}
