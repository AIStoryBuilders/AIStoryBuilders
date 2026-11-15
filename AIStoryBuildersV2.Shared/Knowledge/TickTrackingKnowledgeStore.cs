namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>
/// D12: in WASM the store sits on MEMFS, whose file times are not reliable, so page ticks are kept in memory instead.
/// Every page gets <see cref="DateTime.UtcNow"/> ticks when the story opens, and each write raises its ticks. Conflicts
/// inside a tab work as in KGB; across tabs the zip-level check (plan §5.5) applies.
/// </summary>
public sealed class TickTrackingKnowledgeStore : IKnowledgeStore
{
    readonly IKnowledgeStore _inner;
    readonly Dictionary<string, long> _ticks = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();
    long _last;

    TickTrackingKnowledgeStore(IKnowledgeStore inner) => _inner = inner;

    /// <summary>Wraps <paramref name="inner"/> and seeds the ticks of every existing page.</summary>
    public static async Task<TickTrackingKnowledgeStore> CreateAsync(IKnowledgeStore inner, CancellationToken ct = default)
    {
        var store = new TickTrackingKnowledgeStore(inner);
        var seed = store.Next();
        foreach (var page in await inner.GetAllPagePathsAsync(ct)) store._ticks[page] = seed;
        return store;
    }

    public event Action<KnowledgeChange>? ExternalChange
    {
        add => _inner.ExternalChange += value;
        remove => _inner.ExternalChange -= value;
    }

    /// <summary>Strictly increasing ticks, so two quick saves never share a value.</summary>
    long Next()
    {
        lock (_gate)
        {
            _last = Math.Max(_last + 1, DateTime.UtcNow.Ticks);
            return _last;
        }
    }

    long TicksOf(string pagePath)
    {
        lock (_gate)
        {
            if (!_ticks.TryGetValue(pagePath, out var t)) _ticks[pagePath] = t = Next();
            return t;
        }
    }

    public async Task<IReadOnlyList<KnowledgeNode>> GetChildrenAsync(string folderPath, CancellationToken ct = default)
    {
        var nodes = await _inner.GetChildrenAsync(folderPath, ct);
        return nodes.Select(n => n.IsFolder ? n : n with { ModifiedTicks = TicksOf(n.Path) }).ToList();
    }

    public async Task<PageContent> ReadPageAsync(string pagePath, CancellationToken ct = default)
    {
        var page = await _inner.ReadPageAsync(pagePath, ct);
        return page with { ModifiedTicks = TicksOf(page.Path) };
    }

    public async Task<SaveResult> SavePageAsync(string pagePath, string markdown, long? expectedModifiedTicks, CancellationToken ct = default)
    {
        var path = KnowledgePath.Parse(pagePath, isPage: true).Value;
        var exists = await _inner.ExistsAsync(path, ct);
        var current = exists ? TicksOf(path) : 0;

        if (expectedModifiedTicks == 0 && exists)
            return new SaveResult(SaveStatus.AlreadyExists, current, $"{path} already exists.");
        if (expectedModifiedTicks > 0 && (!exists || current != expectedModifiedTicks))
            return new SaveResult(SaveStatus.Conflict, current, $"{path} changed since it was opened.");

        var result = await _inner.SavePageAsync(path, markdown, null, ct);
        if (result.Status is not (SaveStatus.Saved or SaveStatus.Created)) return result;

        var ticks = Next();
        lock (_gate) _ticks[path] = ticks;
        return result with { ModifiedTicks = ticks };
    }

    public Task CreateFolderAsync(string folderPath, CancellationToken ct = default) => _inner.CreateFolderAsync(folderPath, ct);

    public async Task<string> RenameAsync(string path, string newName, CancellationToken ct = default)
    {
        var newPath = await _inner.RenameAsync(path, newName, ct);
        Rekey(path, newPath);
        return newPath;
    }

    public async Task<string> MoveAsync(string path, string newParentFolder, CancellationToken ct = default)
    {
        var newPath = await _inner.MoveAsync(path, newParentFolder, ct);
        Rekey(path, newPath);
        return newPath;
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        await _inner.DeleteAsync(path, ct);
        var target = KnowledgePath.Parse(path, isPage: false).Value;
        lock (_gate)
        {
            foreach (var key in _ticks.Keys.Where(k => Covers(target, k)).ToList()) _ticks.Remove(key);
        }
    }

    public Task<FolderStats> GetFolderStatsAsync(string folderPath, CancellationToken ct = default) => _inner.GetFolderStatsAsync(folderPath, ct);

    public Task<IReadOnlyList<string>> GetAllPagePathsAsync(CancellationToken ct = default) => _inner.GetAllPagePathsAsync(ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => _inner.ExistsAsync(path, ct);

    /// <summary>A rename or move keeps each page's ticks under its new path (a renamed page counts as written).</summary>
    void Rekey(string rawOld, string newPath)
    {
        var old = KnowledgePath.Parse(rawOld, isPage: false).Value;
        lock (_gate)
        {
            foreach (var key in _ticks.Keys.Where(k => Covers(old, k)).ToList())
            {
                var moved = string.Equals(key, old, StringComparison.OrdinalIgnoreCase) || string.Equals(key, old + KnowledgePath.PageExtension, StringComparison.OrdinalIgnoreCase)
                    ? newPath
                    : newPath + key[old.Length..];
                _ticks.Remove(key);
                _ticks[moved] = Next();
            }
        }
    }

    static bool Covers(string target, string key) =>
        string.Equals(key, target, StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, target + KnowledgePath.PageExtension, StringComparison.OrdinalIgnoreCase)
        || key.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase);
}
