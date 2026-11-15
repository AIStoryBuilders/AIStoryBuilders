using System.Security.Cryptography;
using System.Text;
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Hosting;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;
using System.Runtime.CompilerServices;
using AIStoryBuildersV2.Shared.Settings;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>A unique temporary directory that is deleted on dispose (KGB).</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string relative) => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Path)) return;
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class TestHost(string rootPath) : IHostCapabilities
{
    public bool CanToggleTheme => false;
    public bool CanOpenFolder => false;
    public bool IsBrowser { get; set; }
    public bool HasBackup => false;
    public bool DeletesAreRecoverable => false;
    public string RootDisplay => "Test root";
    public string RootPath => rootPath;
    public void OpenRootFolder() { }
    public bool SupportsSpeechToText => false;
}

public sealed class TestStoryIndexLock : IStoryIndexLock
{
    readonly SemaphoreSlim _lock = new(1, 1);

    public async Task RunAsync(Func<Task> action, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { await action(); }
        finally { _lock.Release(); }
    }
}

/// <summary>Test-Data §2: deterministic 384-number vectors.</summary>
public static class FixtureVectors
{
    public static float[] Of(string text)
    {
        var seed = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0);
        var rng = new Random(seed);
        var v = new float[StoryFormat.VectorDimensions];
        for (var i = 0; i < v.Length; i++) v[i] = (float)(rng.NextDouble() * 2 - 1);
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
        return v;
    }

    public static string Format(string text) => StoryFormat.FormatVector(Of(text));
}

/// <summary>An embedding generator that returns <see cref="FixtureVectors.Of"/> (Test-Data §2).</summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<string> Inputs { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        Inputs.AddRange(list);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(FixtureVectors.Of(v)))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>An <see cref="IStoryPersistence"/> that records MarkDirty calls (plan §10, MarkDirtyCoverageTests).</summary>
public sealed class RecordingPersistence : IStoryPersistence
{
    public List<string> Dirty { get; } = [];
    public Task OpenAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public void MarkDirty(string title) => Dirty.Add(title);
    public Task FlushAsync(string? title = null, CancellationToken ct = default) => Task.CompletedTask;
    public List<string> Deleted { get; } = [];
    public Task DeleteAsync(string title, CancellationToken ct = default) { Deleted.Add(title); return Task.CompletedTask; }
    public Task ReloadAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsDirty(string title) => false;
    public Task<StorageUsage?> GetUsageAsync(CancellationToken ct = default) => Task.FromResult<StorageUsage?>(null);
    public event Action<StoryPersistenceEvent>? Changed { add { } remove { } }
}

/// <summary>An <see cref="IAppDataStore"/> in memory.</summary>
public sealed class MemoryAppDataStore : IAppDataStore
{
    public Dictionary<AppDataFile, string> Files { get; } = [];
    public Task<string?> ReadAsync(AppDataFile file, CancellationToken ct = default) => Task.FromResult(Files.TryGetValue(file, out var v) ? v : null);
    public Task WriteAsync(AppDataFile file, string content, CancellationToken ct = default) { Files[file] = content; return Task.CompletedTask; }
}

/// <summary>An <see cref="AIStoryBuildersService"/> over a temp stories root, with fixture vectors.</summary>
public sealed class ServiceFixture : IDisposable
{
    public ServiceFixture(bool writeBefore = true, IChatClient? chat = null)
    {
        if (writeBefore) FixtureFactory.WriteBefore(Dir.Path);
        Log = new LogService(AppData) { SaveDelay = TimeSpan.FromMinutes(10) };
        Library = new CsvStoryLibraryStore(Dir.Path);
        Chat = chat ?? new ScriptedChatClient();
        Orchestrator = new OrchestratorMethods(Log, Embeddings, new FakeChatClientProvider(Chat), new StubSettings());
        Service = new AIStoryBuildersService(new TestHost(Dir.Path), Library, Persistence, Log, Embeddings, Orchestrator,
            GraphCache, new GraphBuilder(), new AppMetadata { Version = StoryStorageConstants.StybldFormatVersion });
    }

    public IChatClient Chat { get; }
    public OrchestratorMethods Orchestrator { get; }
    public StoryGraphCache GraphCache { get; } = new();

    public TempDir Dir { get; } = new();
    public MemoryAppDataStore AppData { get; } = new();
    public LogService Log { get; }
    public CsvStoryLibraryStore Library { get; }
    public RecordingPersistence Persistence { get; } = new();
    public FakeEmbeddingGenerator Embeddings { get; } = new();
    public AIStoryBuildersService Service { get; }

    public string StoryDir => Dir.Combine(FixtureFactory.Title);

    public Story Story => new() { Title = FixtureFactory.Title };

    public Chapter Chapter(int n) => new() { ChapterName = "Chapter " + n, Sequence = n, Story = Story };

    public void Dispose() => Dir.Dispose();
}

/// <summary>Runs code under a culture and restores the previous one.</summary>
public sealed class CultureScope : IDisposable
{
    readonly System.Globalization.CultureInfo _culture, _ui;

    public CultureScope(string name)
    {
        _culture = System.Globalization.CultureInfo.CurrentCulture;
        _ui = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(name);
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(name);
    }

    public void Dispose()
    {
        System.Globalization.CultureInfo.CurrentCulture = _culture;
        System.Globalization.CultureInfo.CurrentUICulture = _ui;
    }
}

/// <summary>One scripted model response: updates to stream, then optionally an exception.</summary>
public sealed record ScriptStep(IReadOnlyList<ChatResponseUpdate> Updates, Exception? ThrowAfter = null);

/// <summary>An <see cref="IChatClient"/> that plays back queued responses and records what it receives.</summary>
public sealed class ScriptedChatClient : IChatClient
{
    readonly Queue<ScriptStep> _steps = new();
    int _messageCounter;

    public List<List<ChatMessage>> Requests { get; } = [];
    public List<ChatOptions?> Options { get; } = [];

    /// <summary>Used when the queue is empty (null = throw).</summary>
    public Func<ScriptStep>? Fallback { get; set; }

    public int Remaining => _steps.Count;

    public ScriptedChatClient Enqueue(params ChatResponseUpdate[] updates)
    {
        _steps.Enqueue(new ScriptStep(updates));
        return this;
    }

    public ScriptedChatClient EnqueueText(string text) => Enqueue(Text(text));

    public ScriptedChatClient EnqueueCalls(params FunctionCallContent[] calls) => Enqueue(Calls(calls));

    public ScriptedChatClient EnqueueFailure(Exception ex, params ChatResponseUpdate[] before)
    {
        _steps.Enqueue(new ScriptStep(before, ex));
        return this;
    }

    public ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, text) { MessageId = NextId(), FinishReason = ChatFinishReason.Stop };

    public ChatResponseUpdate Calls(params FunctionCallContent[] calls)
    {
        var update = new ChatResponseUpdate { Role = ChatRole.Assistant, MessageId = NextId(), FinishReason = ChatFinishReason.ToolCalls };
        foreach (var c in calls) update.Contents.Add(c);
        return update;
    }

    string NextId() => "msg_" + Interlocked.Increment(ref _messageCounter);

    public static FunctionCallContent Call(string id, string name, params (string Key, object? Value)[] args) =>
        new(id, name, args.ToDictionary(a => a.Key, a => a.Value));

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in GetStreamingResponseAsync(messages, options, cancellationToken)) updates.Add(u);
        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.Select(m => m.Clone()).ToList());
        Options.Add(options);
        ScriptStep step;
        if (_steps.Count > 0) step = _steps.Dequeue();
        else if (Fallback is not null) step = Fallback();
        else throw new InvalidOperationException("The scripted client has no more responses.");

        foreach (var update in step.Updates)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
        if (step.ThrowAfter is not null) throw step.ThrowAfter;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

public sealed class FakeChatClientProvider(IChatClient? client, string? reason = null) : IChatClientProvider
{
    public List<string?> Models { get; } = [];

    public Task<ChatClientResult> GetClientAsync(string? model = null)
    {
        Models.Add(model);
        return Task.FromResult(new ChatClientResult(client, client is null ? reason ?? "Add an API key in Settings" : null));
    }
}

/// <summary>An <see cref="IModelCapabilityProbe"/> that records calls and returns a fixed result.</summary>
public sealed class StubProbe(bool pass = true, ProbeStage failAt = ProbeStage.ToolCall, string message = "The model answered in text and did not call kb_read_page.") : IModelCapabilityProbe
{
    public List<Shared.Llm.LlmOptions> Calls { get; } = [];

    public Task<ModelProbeResult> ProbeAsync(Shared.Llm.LlmOptions options, CancellationToken ct)
    {
        Calls.Add(options);
        return Task.FromResult(pass ? new ModelProbeResult(true, null, null, TimeSpan.Zero) : new ModelProbeResult(false, failAt, message, TimeSpan.Zero));
    }
}

/// <summary>An <see cref="IAppSettingsService"/> with fixed settings.</summary>
public sealed class StubSettings(Shared.Llm.LlmProvider provider = Shared.Llm.LlmProvider.OpenAI, string model = "test-model") : IAppSettingsService
{
    public Task<AppSettings> GetAsync() => Task.FromResult(new AppSettings("Test", provider, model, null, true,
        [new VerifiedModel(provider, null, model, Shared.Llm.ProbeConstants.Version, DateTimeOffset.UtcNow)]));
    public Task<SettingsSaveResult> SaveAsync(Shared.Llm.LlmProvider p, string m, string? e, string? k, CancellationToken ct) => throw new NotSupportedException();
    public Task ClearApiKeyAsync() => Task.CompletedTask;
    public Task<string?> TestConnectionAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<Shared.Llm.ModelListResult> ListModelsAsync(Shared.Llm.LlmProvider p, string? e, string? k, bool r, CancellationToken ct) => Task.FromResult(Shared.Llm.ModelListResult.Success([model]));
    public Task<ModelProbeResult> VerifyModelAsync(string m, CancellationToken ct) => Task.FromResult(new ModelProbeResult(true, null, null, TimeSpan.Zero));
    public Task RemoveVerifiedModelAsync(string m) => Task.CompletedTask;
    public event Action? Changed { add { } remove { } }
}
