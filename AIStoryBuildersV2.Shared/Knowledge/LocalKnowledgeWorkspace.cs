using Microsoft.Extensions.Logging;

namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>
/// The single entry point for the UI and chat tools. One write lock covers every mutation,
/// including a rename and all of its link rewrites.
/// </summary>
public sealed class LocalKnowledgeWorkspace : IKnowledgeWorkspace, IDisposable
{
    readonly IKnowledgeStore _store;
    readonly KnowledgeGraphService _graph;
    readonly ILogger<LocalKnowledgeWorkspace> _log;
    readonly SemaphoreSlim _writeLock = new(1, 1);

    public LocalKnowledgeWorkspace(IKnowledgeStore store, KnowledgeGraphService graph, ILogger<LocalKnowledgeWorkspace> log)
    {
        _store = store;
        _graph = graph;
        _log = log;
        _store.ExternalChange += OnExternalChange;
    }

    public event Action<KnowledgeChange>? Changed;

    public IKnowledgeGraphQuery Graph => _graph;

    public Task<IReadOnlyList<KnowledgeNode>> GetChildrenAsync(string folderPath, CancellationToken ct = default) => _store.GetChildrenAsync(folderPath, ct);

    public Task<PageContent> ReadPageAsync(string path, CancellationToken ct = default) => _store.ReadPageAsync(path, ct);

    public Task<FolderStats> GetFolderStatsAsync(string path, CancellationToken ct = default) => _store.GetFolderStatsAsync(path, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => _store.ExistsAsync(path, ct);

    public Task<IReadOnlyList<string>> GetAllPagePathsAsync(CancellationToken ct = default) => _store.GetAllPagePathsAsync(ct);

    public async Task<SaveResult> SavePageAsync(string path, string markdown, long? expectedModifiedTicks, CancellationToken ct = default)
    {
        var page = KnowledgePath.Parse(path, isPage: true).Value;
        SaveResult result;
        await _writeLock.WaitAsync(ct);
        try
        {
            result = await _store.SavePageAsync(page, markdown, expectedModifiedTicks, ct);
            if (result.Status is SaveStatus.Saved or SaveStatus.Created)
                await _graph.UpdatePageAsync(page, markdown, result.ModifiedTicks, ct);
        }
        finally { _writeLock.Release(); }

        if (result.Status is SaveStatus.Saved or SaveStatus.Created)
        {
            _log.LogInformation("Saved {Path} ({Status})", page, result.Status);
            Raise(new KnowledgeChange(result.Status == SaveStatus.Created ? ChangeKind.Created : ChangeKind.Updated, page));
        }
        return result;
    }

    public async Task CreateFolderAsync(string path, CancellationToken ct = default)
    {
        var folder = KnowledgePath.Parse(path, isPage: false).Value;
        await _writeLock.WaitAsync(ct);
        try { await _store.CreateFolderAsync(folder, ct); }
        finally { _writeLock.Release(); }
        Raise(new KnowledgeChange(ChangeKind.FolderCreated, folder));
    }

    public Task<RenameResult> RenameAsync(string path, string newName, CancellationToken ct = default) =>
        RelocateAsync(path, ChangeKind.Renamed, () => _store.RenameAsync(path, newName, ct), ct);

    public Task<RenameResult> MoveAsync(string path, string newParentFolder, CancellationToken ct = default) =>
        RelocateAsync(path, ChangeKind.Moved, () => _store.MoveAsync(path, newParentFolder, ct), ct);

    async Task<RenameResult> RelocateAsync(string path, ChangeKind kind, Func<Task<string>> relocate, CancellationToken ct)
    {
        RenameResult result;
        string oldPath;
        await _writeLock.WaitAsync(ct);
        try
        {
            if (!_graph.IsBuilt) await _graph.RebuildAsync(ct);
            var oldSnapshot = _graph.Snapshot;
            var allPagesBefore = await _store.GetAllPagePathsAsync(ct);

            var newPath = await relocate();
            oldPath = ResolveOldPath(path, newPath, allPagesBefore);
            if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
                return new RenameResult(newPath, 0, []);

            var moved = LinkRewriter.MapMovedPages(allPagesBefore, oldPath, newPath);
            var newDirectory = new PageDirectory(allPagesBefore.Select(p => moved.TryGetValue(p, out var n) ? n : p));

            var changed = 0;
            var failed = new List<string>();
            foreach (var oldSource in LinkRewriter.AffectedPages(oldSnapshot, moved))
            {
                var current = moved.TryGetValue(oldSource, out var n) ? n : oldSource;
                try
                {
                    var content = await _store.ReadPageAsync(current, ct);
                    var rewritten = LinkRewriter.Rewrite(content.Markdown, oldSource, oldSnapshot, moved, newDirectory);
                    if (rewritten == content.Markdown) continue;
                    var save = await _store.SavePageAsync(current, rewritten, content.ModifiedTicks, ct);
                    if (save.Status == SaveStatus.Saved) changed++;
                    else failed.Add(current);
                }
                catch (KnowledgeException ex)
                {
                    _log.LogWarning("Couldn't update links in {Path}: {Message}", current, ex.Message);
                    failed.Add(current);
                }
            }

            await _graph.RemovePathAsync(oldPath, ct);
            await _graph.RebuildAsync(ct);
            result = new RenameResult(newPath, changed, failed);
        }
        finally { _writeLock.Release(); }

        _log.LogInformation("{Kind} {Old} to {New}; {Count} pages updated", kind, oldPath, result.NewPath, result.PagesChanged);
        Raise(new KnowledgeChange(kind, result.NewPath, oldPath));
        return result;
    }

    static string ResolveOldPath(string raw, string newPath, IReadOnlyList<string> pagesBefore)
    {
        var p = KnowledgePath.Parse(raw, isPage: false).Value;
        if (KnowledgePath.IsPagePath(newPath) && !KnowledgePath.IsPagePath(p)
            && pagesBefore.Contains(p + KnowledgePath.PageExtension, StringComparer.OrdinalIgnoreCase))
            return p + KnowledgePath.PageExtension;
        return p;
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var target = KnowledgePath.Parse(path, isPage: false).Value;
        await _writeLock.WaitAsync(ct);
        try
        {
            await _store.DeleteAsync(target, ct);
            await _graph.RemovePathAsync(target, ct);
        }
        finally { _writeLock.Release(); }
        _log.LogInformation("Deleted {Path}", target);
        Raise(new KnowledgeChange(ChangeKind.Deleted, target));
    }

    void OnExternalChange(KnowledgeChange change)
    {
        _ = HandleExternalAsync(change);
    }

    async Task HandleExternalAsync(KnowledgeChange change)
    {
        try
        {
            switch (change.Kind)
            {
                case ChangeKind.Created or ChangeKind.Updated:
                    await _graph.UpdatePageAsync(change.Path);
                    break;
                case ChangeKind.Deleted:
                    await _graph.RemovePathAsync(change.Path);
                    break;
                default:
                    _graph.Invalidate();
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning("Graph update after an external change failed: {Message}", ex.Message);
            _graph.Invalidate();
        }
        Raise(change);
    }

    void Raise(KnowledgeChange change)
    {
        try { Changed?.Invoke(change); }
        catch (Exception ex) { _log.LogError(ex, "A Changed handler failed"); }
    }

    public void Dispose() => _store.ExternalChange -= OnExternalChange;
}
