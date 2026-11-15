using AIStoryBuildersV2.Shared.Knowledge.Persistence;

namespace AIStoryBuildersV2.Shared.Stories.Storage;

/// <summary>MAUI: V1's AIStoryBuildersLog.csv in the stories root, shared with V1.</summary>
public sealed class FileAppDataStore(string rootPath) : IAppDataStore
{
    public string PathOf(AppDataFile file) => Path.Combine(rootPath, file switch
    {
        AppDataFile.Log => "AIStoryBuildersLog.csv",
        _ => throw new ArgumentOutOfRangeException(nameof(file)),
    });

    public async Task<string?> ReadAsync(AppDataFile file, CancellationToken ct = default)
    {
        var path = PathOf(file);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
    }

    public Task WriteAsync(AppDataFile file, string content, CancellationToken ct = default) =>
        File.WriteAllTextAsync(PathOf(file), content, StoryFormat.Utf8NoBom, ct);
}

/// <summary>WASM: asb2:log in localStorage.</summary>
public sealed class LocalStorageAppDataStore(IKeyValueStore store) : IAppDataStore
{
    static string KeyOf(AppDataFile file) => file switch
    {
        AppDataFile.Log => StoryStorageConstants.LogKey,
        _ => throw new ArgumentOutOfRangeException(nameof(file)),
    };

    public Task<string?> ReadAsync(AppDataFile file, CancellationToken ct = default) => store.GetAsync(KeyOf(file));

    public Task WriteAsync(AppDataFile file, string content, CancellationToken ct = default) => store.SetAsync(KeyOf(file), content);
}
