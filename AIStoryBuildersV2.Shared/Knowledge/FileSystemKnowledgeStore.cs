using System.Text;

namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>
/// Stores pages as .md files under a root folder. In WASM the root is in the .NET in-memory file system.
/// Not thread-safe on its own: <see cref="LocalKnowledgeWorkspace"/> serializes all writes.
/// </summary>
public sealed class FileSystemKnowledgeStore : IKnowledgeStore
{
    const string RestoreSuffix = ".restore";
    static readonly UTF8Encoding Utf8NoBom = new(false);

    readonly string _root;
    readonly int _maxPageBytes;
    readonly IFileDeleter _deleter;

    public FileSystemKnowledgeStore(KnowledgeStoreOptions options, IFileDeleter deleter)
    {
        _root = Path.GetFullPath(options.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _maxPageBytes = options.MaxPageBytes;
        _deleter = deleter;
        // The root is created by the first write, so V1-format stories without a KB stay unchanged (plan §5.7).
    }

    public string RootPath => _root;

    public IFileDeleter Deleter => _deleter;

#pragma warning disable CS0067 // The file system store has no watcher (D5).
    public event Action<KnowledgeChange>? ExternalChange;
#pragma warning restore CS0067

    public Task<IReadOnlyList<KnowledgeNode>> GetChildrenAsync(string folderPath, CancellationToken ct = default)
    {
        var folder = KnowledgePath.Parse(folderPath, isPage: false);
        var full = ToFull(folder);
        if (folder.IsRoot && !Directory.Exists(full)) return Task.FromResult<IReadOnlyList<KnowledgeNode>>([]);
        if (!Directory.Exists(full)) throw new KnowledgeNotFoundException(folder.Value);

        var dir = new DirectoryInfo(full);
        var folders = new List<KnowledgeNode>();
        var pages = new List<KnowledgeNode>();
        foreach (var entry in dir.EnumerateFileSystemInfos())
        {
            if (!IsVisible(entry)) continue;
            if (entry is DirectoryInfo d)
                folders.Add(new KnowledgeNode(d.Name, folder.Combine(d.Name).Value, true, d.LastWriteTimeUtc.Ticks));
            else if (entry is FileInfo f && KnowledgePath.IsPagePath(f.Name))
                pages.Add(new KnowledgeNode(f.Name, folder.Combine(f.Name).Value, false, f.LastWriteTimeUtc.Ticks));
        }

        folders.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        pages.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return Task.FromResult<IReadOnlyList<KnowledgeNode>>([.. folders, .. pages]);
    }

    public async Task<PageContent> ReadPageAsync(string pagePath, CancellationToken ct = default)
    {
        var path = KnowledgePath.Parse(pagePath, isPage: true);
        var full = ToFull(path);
        if (!File.Exists(full)) throw new KnowledgeNotFoundException(path.Value);
        try
        {
            var text = await File.ReadAllTextAsync(full, Utf8NoBom, ct);
            return new PageContent(path.Value, text, File.GetLastWriteTimeUtc(full).Ticks);
        }
        catch (IOException ex)
        {
            throw new KnowledgeIoException($"Couldn't read {path.Value}: {ex.Message}", ex);
        }
    }

    public async Task<SaveResult> SavePageAsync(string pagePath, string markdown, long? expectedModifiedTicks, CancellationToken ct = default)
    {
        var path = KnowledgePath.Parse(pagePath, isPage: true);
        var bytes = Utf8NoBom.GetByteCount(markdown);
        if (bytes > _maxPageBytes) throw new PageTooLargeException(bytes);

        var full = ToFull(path);
        var exists = File.Exists(full);
        var currentTicks = exists ? File.GetLastWriteTimeUtc(full).Ticks : 0;

        if (expectedModifiedTicks == 0 && exists)
            return new SaveResult(SaveStatus.AlreadyExists, currentTicks, $"{path.Value} already exists.");
        if (expectedModifiedTicks > 0 && (!exists || currentTicks != expectedModifiedTicks))
            return new SaveResult(SaveStatus.Conflict, currentTicks, $"{path.Value} changed since it was opened.");

        var parent = Path.GetDirectoryName(full)!;
        if (Directory.Exists(parent) is false)
        {
            EnsureNoFileInTheWay(path.Parent);
            Directory.CreateDirectory(parent);
        }

        try
        {
            await File.WriteAllTextAsync(full, markdown, Utf8NoBom, ct);
            var newTicks = NextTicks(currentTicks);
            File.SetLastWriteTimeUtc(full, new DateTime(newTicks, DateTimeKind.Utc));
            newTicks = File.GetLastWriteTimeUtc(full).Ticks;
            return new SaveResult(exists ? SaveStatus.Saved : SaveStatus.Created, newTicks);
        }
        catch (IOException ex)
        {
            throw new KnowledgeIoException($"Couldn't save {path.Value}: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new KnowledgeIoException($"Couldn't save {path.Value}: {ex.Message}", ex);
        }
    }

    /// <summary>Writes a page with a specific modification time (used by hydration and import).</summary>
    internal void WriteRaw(KnowledgePath path, string markdown, long ticks)
    {
        var full = ToFull(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, markdown, Utf8NoBom);
        if (IsFileTime(ticks)) File.SetLastWriteTimeUtc(full, new DateTime(ticks, DateTimeKind.Utc));
    }

    internal void SetTicks(KnowledgePath path, long ticks)
    {
        var full = ToFull(path);
        if (File.Exists(full) && IsFileTime(ticks)) File.SetLastWriteTimeUtc(full, new DateTime(ticks, DateTimeKind.Utc));
    }

    /// <summary>Ticks from storage may be anything; file times can't be before 1601.</summary>
    static bool IsFileTime(long ticks) => ticks >= MinFileTimeTicks && ticks <= DateTime.MaxValue.Ticks;

    static readonly long MinFileTimeTicks = DateTime.FromFileTimeUtc(0).Ticks;

    internal void DeleteRaw(KnowledgePath path)
    {
        var full = ToFull(path);
        if (File.Exists(full)) File.Delete(full);
        else if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }

    internal void ResetRoot()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        Directory.CreateDirectory(_root);
    }

    public Task CreateFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var path = KnowledgePath.Parse(folderPath, isPage: false);
        if (path.IsRoot) return Task.CompletedTask;
        var full = ToFull(path);
        if (Directory.Exists(full) || File.Exists(full)) throw new KnowledgeAlreadyExistsException(path.Value);
        EnsureNoFileInTheWay(path.Parent);
        Directory.CreateDirectory(full);
        return Task.CompletedTask;
    }

    public Task<string> RenameAsync(string path, string newName, CancellationToken ct = default)
    {
        var (source, isPage) = ResolveExisting(path);
        if (source.IsRoot) throw new InvalidKnowledgePathException("The root can't be renamed.");

        newName = (newName ?? "").Trim();
        if (isPage && !KnowledgePath.IsPagePath(newName)) newName += KnowledgePath.PageExtension;
        if (newName.Contains('/') || newName.Contains('\\')) throw new InvalidKnowledgePathException("Names can't contain '/' or '\\'.");
        var error = KnowledgePath.ValidateSegment(newName);
        if (error is not null) throw new InvalidKnowledgePathException(error);

        var target = KnowledgePath.Parse(source.Parent.Combine(newName).Value, isPage);
        if (string.Equals(target.Value, source.Value, StringComparison.Ordinal)) return Task.FromResult(source.Value);

        MoveEntry(source, target, isPage);
        return Task.FromResult(target.Value);
    }

    public Task<string> MoveAsync(string path, string newParentFolder, CancellationToken ct = default)
    {
        var (source, isPage) = ResolveExisting(path);
        if (source.IsRoot) throw new InvalidKnowledgePathException("The root can't be moved.");
        var parent = KnowledgePath.Parse(newParentFolder, isPage: false);
        if (!Directory.Exists(ToFull(parent))) throw new KnowledgeNotFoundException(parent.Value);
        if (!isPage && (parent == source || parent.IsUnder(source)))
            throw new InvalidKnowledgePathException("A folder can't be moved into itself or one of its subfolders.");

        var target = KnowledgePath.Parse(parent.Combine(source.Name).Value, isPage);
        if (string.Equals(target.Value, source.Value, StringComparison.Ordinal)) return Task.FromResult(source.Value);
        MoveEntry(source, target, isPage);
        return Task.FromResult(target.Value);
    }

    void MoveEntry(KnowledgePath source, KnowledgePath target, bool isPage)
    {
        var from = ToFull(source);
        var to = ToFull(target);
        var caseOnly = string.Equals(source.Value, target.Value, StringComparison.OrdinalIgnoreCase);

        if (!caseOnly && (File.Exists(to) || Directory.Exists(to)))
            throw new KnowledgeAlreadyExistsException(target.Value);

        try
        {
            if (caseOnly)
            {
                // Case-only renames go through a temporary name; the sidecar lets startup recover from a crash.
                var tempName = KnowledgePath.TempPrefix + Guid.NewGuid().ToString("N");
                var temp = Path.Combine(Path.GetDirectoryName(from)!, tempName);
                var sidecar = temp + RestoreSuffix;
                File.WriteAllText(sidecar, source.Name, Utf8NoBom);
                Move(from, temp, isPage);
                Move(temp, to, isPage);
                File.Delete(sidecar);
            }
            else
            {
                Move(from, to, isPage);
            }
        }
        catch (IOException ex)
        {
            throw new KnowledgeIoException($"Couldn't move {source.Value}: {ex.Message}", ex);
        }
    }

    static void Move(string from, string to, bool isPage)
    {
        if (isPage) File.Move(from, to);
        else Directory.Move(from, to);
    }

    /// <summary>Recovers entries left with a temporary name by an interrupted case-only rename.</summary>
    public int RecoverInterruptedRenames()
    {
        var recovered = 0;
        if (!Directory.Exists(_root)) return 0;
        foreach (var sidecar in Directory.EnumerateFiles(_root, KnowledgePath.TempPrefix + "*" + RestoreSuffix, SearchOption.AllDirectories))
        {
            var temp = sidecar[..^RestoreSuffix.Length];
            var originalName = File.ReadAllText(sidecar).Trim();
            var original = Path.Combine(Path.GetDirectoryName(sidecar)!, originalName);
            if (KnowledgePath.ValidateSegment(originalName) is null)
            {
                if (File.Exists(temp) && !File.Exists(original)) { File.Move(temp, original); recovered++; }
                else if (Directory.Exists(temp) && !Directory.Exists(original)) { Directory.Move(temp, original); recovered++; }
            }
            File.Delete(sidecar);
        }
        return recovered;
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var (target, isPage) = ResolveExisting(path);
        if (target.IsRoot) throw new InvalidKnowledgePathException("The root can't be deleted.");
        var full = ToFull(target);
        try
        {
            if (isPage) _deleter.DeleteFile(full);
            else _deleter.DeleteDirectory(full);
        }
        catch (IOException ex)
        {
            throw new KnowledgeIoException($"Couldn't delete {target.Value}: {ex.Message}", ex);
        }
        return Task.CompletedTask;
    }

    public Task<FolderStats> GetFolderStatsAsync(string folderPath, CancellationToken ct = default)
    {
        var (target, isPage) = ResolveExisting(folderPath);
        if (isPage) return Task.FromResult(new FolderStats(1, 0, 0));
        if (!Directory.Exists(ToFull(target))) return Task.FromResult(new FolderStats(0, 0, 0));

        int pages = 0, folders = 0, other = 0;
        foreach (var entry in new DirectoryInfo(ToFull(target)).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if (entry is DirectoryInfo) folders++;
            else if (KnowledgePath.IsPagePath(entry.Name) && IsVisiblePath(entry.FullName)) pages++;
            else other++;
        }
        return Task.FromResult(new FolderStats(pages, folders, other));
    }

    public Task<IReadOnlyList<string>> GetAllPagePathsAsync(CancellationToken ct = default)
    {
        var result = new List<string>();
        if (Directory.Exists(_root)) Walk(new DirectoryInfo(_root), KnowledgePath.Root, result, null);
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    /// <summary>All visible folder paths (used by the persistence index).</summary>
    public IReadOnlyList<string> GetAllFolderPaths()
    {
        var folders = new List<string>();
        if (Directory.Exists(_root)) Walk(new DirectoryInfo(_root), KnowledgePath.Root, new List<string>(), folders);
        return folders;
    }

    void Walk(DirectoryInfo dir, KnowledgePath rel, List<string> pages, List<string>? folders)
    {
        foreach (var entry in dir.EnumerateFileSystemInfos())
        {
            if (!IsVisible(entry)) continue;
            var child = rel.Combine(entry.Name);
            if (entry is DirectoryInfo d)
            {
                folders?.Add(child.Value);
                Walk(d, child, pages, folders);
            }
            else if (KnowledgePath.IsPagePath(entry.Name))
            {
                pages.Add(child.Value);
            }
        }
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        if (!KnowledgePath.TryParse(path, isPage: false, out var p, out _)) return Task.FromResult(false);
        var full = ToFull(p);
        return Task.FromResult(File.Exists(full) || Directory.Exists(full));
    }

    public string ToFullPath(string path) => ToFull(KnowledgePath.Parse(path, isPage: false));

    (KnowledgePath Path, bool IsPage) ResolveExisting(string raw)
    {
        var path = KnowledgePath.Parse(raw, isPage: false);
        var full = ToFull(path);
        if (path.IsRoot) return (path, false);
        if (Directory.Exists(full))
        {
            RefuseLink(full, path);
            return (path, false);
        }
        if (File.Exists(full))
        {
            RefuseLink(full, path);
            return (path, true);
        }
        if (!KnowledgePath.IsPagePath(raw))
        {
            var page = KnowledgePath.Parse(raw, isPage: true);
            if (File.Exists(ToFull(page))) return (page, true);
        }
        throw new KnowledgeNotFoundException(path.Value);
    }

    static void RefuseLink(string full, KnowledgePath path)
    {
        FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
        if (info.LinkTarget is not null) throw new InvalidKnowledgePathException($"{path.Value} is a symbolic link or junction.");
    }

    void EnsureNoFileInTheWay(KnowledgePath folder)
    {
        for (var p = folder; !p.IsRoot; p = p.Parent)
        {
            if (File.Exists(ToFull(p))) throw new KnowledgeAlreadyExistsException(p.Value);
        }
    }

    string ToFull(KnowledgePath path)
    {
        if (path.IsRoot) return _root;
        var full = Path.GetFullPath(Path.Combine(_root, path.Value.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidKnowledgePathException("The path is outside the knowledge root.");
        return full;
    }

    static bool IsVisible(FileSystemInfo entry)
    {
        if (entry.Name.StartsWith('.') || entry.Name.StartsWith(KnowledgePath.TempPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (entry.LinkTarget is not null) return false;
        if (OperatingSystem.IsWindows() && entry.Attributes.HasFlag(FileAttributes.Hidden)) return false;
        return true;
    }

    bool IsVisiblePath(string fullPath)
    {
        var rel = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
        return rel.Split('/').All(s => !s.StartsWith('.') && !s.StartsWith(KnowledgePath.TempPrefix, StringComparison.OrdinalIgnoreCase));
    }

    static long NextTicks(long previous)
    {
        // Whole milliseconds keep the value stable on file systems with coarser timestamps.
        var now = DateTime.UtcNow.Ticks;
        now -= now % TimeSpan.TicksPerMillisecond;
        return Math.Max(now, previous + TimeSpan.TicksPerMillisecond);
    }
}
