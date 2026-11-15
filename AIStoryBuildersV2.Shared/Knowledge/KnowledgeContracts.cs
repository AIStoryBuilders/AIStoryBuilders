namespace AIStoryBuildersV2.Shared.Knowledge;

public sealed record KnowledgeNode(string Name, string Path, bool IsFolder, long ModifiedTicks);

public sealed record PageContent(string Path, string Markdown, long ModifiedTicks);

public enum SaveStatus { Saved, Created, Conflict, AlreadyExists }

public sealed record SaveResult(SaveStatus Status, long ModifiedTicks, string? Message = null);

public sealed record FolderStats(int Pages, int Folders, int OtherFiles);

public sealed record KnowledgeStoreOptions(string RootPath, int MaxPageBytes = 1_048_576);

public enum ChangeKind { Created, Updated, Deleted, Renamed, Moved, FolderCreated, Reloaded }

public sealed record KnowledgeChange(ChangeKind Kind, string Path, string? OldPath = null);

public sealed record RenameResult(string NewPath, int PagesChanged, IReadOnlyList<string> FailedPages);

public class KnowledgeException(string message) : Exception(message);

public sealed class InvalidKnowledgePathException(string m) : KnowledgeException(m);

public sealed class KnowledgeNotFoundException(string path) : KnowledgeException($"Not found: {path}")
{
    public string Path { get; } = path;
}

public sealed class KnowledgeAlreadyExistsException(string path) : KnowledgeException($"Already exists: {path}")
{
    public string Path { get; } = path;
}

public sealed class PageTooLargeException(long bytes) : KnowledgeException($"The page is {bytes:N0} bytes, which is over the 1 MB limit.")
{
    public long Bytes { get; } = bytes;
}

public sealed class KnowledgeIoException(string m, Exception inner) : KnowledgeException(m)
{
    public Exception Inner { get; } = inner;
}

public sealed class StorageQuotaExceededException(long estimatedBytes) : KnowledgeException($"Browser storage is full (about {estimatedBytes:N0} bytes in use).")
{
    public long EstimatedBytes { get; } = estimatedBytes;
}

public interface IKnowledgeStore
{
    Task<IReadOnlyList<KnowledgeNode>> GetChildrenAsync(string folderPath, CancellationToken ct = default);
    Task<PageContent> ReadPageAsync(string pagePath, CancellationToken ct = default);

    /// <param name="expectedModifiedTicks">null = unconditional, 0 = must not exist, &gt;0 = must match.</param>
    Task<SaveResult> SavePageAsync(string pagePath, string markdown, long? expectedModifiedTicks, CancellationToken ct = default);
    Task CreateFolderAsync(string folderPath, CancellationToken ct = default);
    Task<string> RenameAsync(string path, string newName, CancellationToken ct = default);
    Task<string> MoveAsync(string path, string newParentFolder, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<FolderStats> GetFolderStatsAsync(string folderPath, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllPagePathsAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    event Action<KnowledgeChange>? ExternalChange;
}

public interface IFileDeleter
{
    void DeleteFile(string fullPath);
    void DeleteDirectory(string fullPath);

    /// <summary>True when deleted items can be recovered (Recycle Bin).</summary>
    bool IsRecoverable { get; }
}

public sealed class PermanentFileDeleter : IFileDeleter
{
    public bool IsRecoverable => false;
    public void DeleteFile(string fullPath) => File.Delete(fullPath);
    public void DeleteDirectory(string fullPath) => Directory.Delete(fullPath, recursive: true);
}

public interface IKnowledgeWorkspace
{
    event Action<KnowledgeChange>? Changed;
    IKnowledgeGraphQuery Graph { get; }
    Task<IReadOnlyList<KnowledgeNode>> GetChildrenAsync(string folderPath, CancellationToken ct = default);
    Task<PageContent> ReadPageAsync(string path, CancellationToken ct = default);
    Task<SaveResult> SavePageAsync(string path, string markdown, long? expectedModifiedTicks, CancellationToken ct = default);
    Task CreateFolderAsync(string path, CancellationToken ct = default);
    Task<RenameResult> RenameAsync(string path, string newName, CancellationToken ct = default);
    Task<RenameResult> MoveAsync(string path, string newParentFolder, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<FolderStats> GetFolderStatsAsync(string path, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllPagePathsAsync(CancellationToken ct = default);
}
