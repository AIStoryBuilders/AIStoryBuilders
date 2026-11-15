using System.Collections.Concurrent;

namespace AIStoryBuildersV2.Shared.Graph;

/// <summary>V1's GraphState fields, held per story (D17).</summary>
public sealed class StoryGraphEntry
{
    public Models.StoryGraph? Current { get; set; }
    public Story? CurrentStory { get; set; }

    /// <summary>True when <see cref="Current"/> no longer matches the story files and must be rebuilt.</summary>
    public bool IsDirty { get; set; } = true;
}

/// <summary>
/// Replaces V1's static GraphState (D17): the graph, the story and a dirty flag for each story title, so several MAUI
/// windows and stories never share state.
/// </summary>
public sealed class StoryGraphCache
{
    readonly ConcurrentDictionary<string, StoryGraphEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public StoryGraphEntry Get(string title) => _entries.GetOrAdd(title, _ => new StoryGraphEntry());

    public bool TryGet(string title, out StoryGraphEntry entry) => _entries.TryGetValue(title, out entry!);

    public void MarkDirty(string title) => Get(title).IsDirty = true;

    public void Remove(string title) => _entries.TryRemove(title, out _);

    /// <summary>The entry for <see cref="StoryGraphContext.Current"/> (R18: never guess the story).</summary>
    public StoryGraphEntry Current =>
        Get(StoryGraphContext.Current ?? throw new InvalidOperationException("No story in StoryGraphContext"));
}

/// <summary>
/// The story a graph service works on (D26). Set with <c>using (StoryGraphContext.Use(title))</c> at each entry point: a chat
/// turn, CreateMasterStory, ReloadStoryAsync and EnsureGraphExistsAsync. It flows into tool calls made by
/// FunctionInvokingChatClient on the same async path.
/// </summary>
public static class StoryGraphContext
{
    static readonly AsyncLocal<string?> _current = new();

    public static string? Current => _current.Value;

    public static IDisposable Use(string title)
    {
        var previous = _current.Value;
        _current.Value = title;
        return new Restore(previous);
    }

    sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
