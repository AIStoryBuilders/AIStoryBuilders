using AIStoryBuildersV2.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>One story's knowledge base (plan §6.4): KGB's store, graph and workspace for <c>{Title}/KnowledgeBase</c>.</summary>
public interface IStoryKnowledgeBase : IAsyncDisposable
{
    string StoryTitle { get; }

    /// <summary><c>{StoriesRoot}/{Title}/KnowledgeBase</c>; created by the first write.</summary>
    string RootPath { get; }
    IKnowledgeWorkspace Workspace { get; }
    IKnowledgeGraphIndex Graph { get; }

    /// <summary>KGB's UI state (unsaved paths, expanded folders), one per story (D13).</summary>
    WorkspaceState State { get; }
    KbLinkBuilder Links { get; }
    Task<bool> IsEmptyAsync(CancellationToken ct = default);
}

public interface IStoryKnowledgeBaseProvider
{
    /// <summary>Opens (or returns the cached) KB for <paramref name="storyTitle"/>.</summary>
    Task<IStoryKnowledgeBase> OpenAsync(string storyTitle, CancellationToken ct = default);
    Task CloseAsync(string storyTitle);
}

/// <summary>Scoped (D13): the open story's KB for KB components and dialogs. <c>StoryWorkspace</c> sets it.</summary>
public interface ICurrentStoryKnowledgeBase
{
    IStoryKnowledgeBase? Current { get; set; }
    event Action? CurrentChanged;
}

public sealed class CurrentStoryKnowledgeBase : ICurrentStoryKnowledgeBase
{
    IStoryKnowledgeBase? _current;

    public IStoryKnowledgeBase? Current
    {
        get => _current;
        set
        {
            if (ReferenceEquals(_current, value)) return;
            _current = value;
            CurrentChanged?.Invoke();
        }
    }

    public event Action? CurrentChanged;
}

sealed class StoryKnowledgeBase : IStoryKnowledgeBase
{
    readonly LocalKnowledgeWorkspace _workspace;
    readonly KnowledgeGraphService _graph;
    readonly IKnowledgeStore _store;
    readonly Action<KnowledgeChange> _onChanged;

    public StoryKnowledgeBase(string title, string root, IKnowledgeStore store, ILogger<LocalKnowledgeWorkspace> log, Action markDirty)
    {
        StoryTitle = title;
        RootPath = root;
        _store = store;
        _graph = new KnowledgeGraphService(store);
        _workspace = new LocalKnowledgeWorkspace(store, _graph, log);
        Links = new KbLinkBuilder(title);
        // Every KB write marks the story dirty so WASM saves the zip (plan §5.7).
        _onChanged = _ => markDirty();
        _workspace.Changed += _onChanged;
    }

    public string StoryTitle { get; }
    public string RootPath { get; }
    public IKnowledgeWorkspace Workspace => _workspace;
    public IKnowledgeGraphIndex Graph => _graph;
    public WorkspaceState State { get; } = new();
    public KbLinkBuilder Links { get; }

    public async Task<bool> IsEmptyAsync(CancellationToken ct = default) => (await _store.GetAllPagePathsAsync(ct)).Count == 0;

    public ValueTask DisposeAsync()
    {
        _workspace.Changed -= _onChanged;
        _workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// D12: builds KGB's KB stack per story and caches it by title. In WASM the store is wrapped in
/// <see cref="TickTrackingKnowledgeStore"/>. Opens are counted, so two MAUI windows on one story share a KB and the last
/// close disposes it.
/// </summary>
public sealed class StoryKnowledgeBaseProvider(IHostCapabilities host, IFileDeleter deleter, IStoryPersistence persistence, ILoggerFactory? loggers = null)
    : IStoryKnowledgeBaseProvider
{
    public const string FolderName = "KnowledgeBase";

    sealed class Entry(Lazy<Task<IStoryKnowledgeBase>> kb)
    {
        public Lazy<Task<IStoryKnowledgeBase>> Kb { get; } = kb;
        public int Users;
    }

    readonly Dictionary<string, Entry> _open = new(StringComparer.OrdinalIgnoreCase);

    public static string RootFor(string storiesRoot, string storyTitle) => Path.Combine(storiesRoot, storyTitle, FolderName);

    public Task<IStoryKnowledgeBase> OpenAsync(string storyTitle, CancellationToken ct = default)
    {
        Entry entry;
        lock (_open)
        {
            if (!_open.TryGetValue(storyTitle, out entry!))
                _open[storyTitle] = entry = new Entry(new Lazy<Task<IStoryKnowledgeBase>>(() => CreateAsync(storyTitle, ct)));
            entry.Users++;
        }
        return entry.Kb.Value;
    }

    async Task<IStoryKnowledgeBase> CreateAsync(string storyTitle, CancellationToken ct)
    {
        var root = RootFor(host.RootPath, storyTitle);
        var fileStore = new FileSystemKnowledgeStore(new KnowledgeStoreOptions(root), deleter);
        fileStore.RecoverInterruptedRenames();
        IKnowledgeStore store = host.IsBrowser ? await TickTrackingKnowledgeStore.CreateAsync(fileStore, ct) : fileStore;
        var log = (loggers ?? NullLoggerFactory.Instance).CreateLogger<LocalKnowledgeWorkspace>();
        return new StoryKnowledgeBase(storyTitle, root, store, log, () => persistence.MarkDirty(storyTitle));
    }

    public async Task CloseAsync(string storyTitle)
    {
        Entry? entry;
        lock (_open)
        {
            if (!_open.TryGetValue(storyTitle, out entry)) return;
            if (--entry.Users > 0) return;
            _open.Remove(storyTitle);
        }
        try
        {
            var kb = await entry.Kb.Value;
            await kb.DisposeAsync();
        }
        catch (Exception)
        {
            // A KB that failed to open has nothing to dispose.
        }
    }
}
