namespace AIStoryBuildersV2.Shared.Services;

/// <summary>UI state shared by the panes: unsaved pages, layout width and pending approvals.</summary>
public sealed class WorkspaceState
{
    readonly HashSet<string> _unsaved = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public IReadOnlyCollection<string> UnsavedPaths => _unsaved;

    public bool IsUnsaved(string path) => _unsaved.Contains(path);

    public bool HasUnsaved => _unsaved.Count > 0;

    /// <summary>Expanded explorer folders; kept here so they survive switching between wide and narrow layouts.</summary>
    public HashSet<string> ExpandedFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsWide { get; private set; } = true;

    public int PendingApprovals { get; private set; }

    /// <summary>Narrow layout: 0 Explorer, 1 Editor, 2 Chat.</summary>
    public int ActiveTab { get; private set; } = 1;

    /// <summary>Set by the editor so other parts of the UI can save before leaving.</summary>
    public Func<Task<bool>>? SaveCurrentAsync { get; set; }

    public void SetUnsaved(string path, bool unsaved)
    {
        var changed = unsaved ? _unsaved.Add(path) : _unsaved.Remove(path);
        if (changed) Changed?.Invoke();
    }

    public void RenamePath(string oldPath, string newPath)
    {
        foreach (var p in _unsaved.ToList())
        {
            if (string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase))
            {
                _unsaved.Remove(p);
                _unsaved.Add(newPath);
            }
            else if (p.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                _unsaved.Remove(p);
                _unsaved.Add(newPath + p[oldPath.Length..]);
            }
        }
        Changed?.Invoke();
    }

    public void SetWide(bool wide)
    {
        if (IsWide == wide) return;
        IsWide = wide;
        Changed?.Invoke();
    }

    public void SetPendingApprovals(int count)
    {
        if (PendingApprovals == count) return;
        PendingApprovals = count;
        Changed?.Invoke();
    }

    public void SetActiveTab(int tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab = tab;
        Changed?.Invoke();
    }
}
