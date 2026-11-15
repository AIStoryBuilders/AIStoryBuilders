using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace AIStoryBuildersV2.Shared.StoryChat;

/// <summary>
/// A change the model proposed in Story Chat, with its arguments frozen in <see cref="Apply"/>. Only the user can apply it,
/// from the chat UI (SEC-001): the model never sees a <c>confirmed</c> parameter. <see cref="Arguments"/> are the
/// tool-call arguments as text, so the user sees exactly what will be written.
/// </summary>
public sealed record PendingMutation(string Id, string StoryTitle, string ToolName, MutationResult Preview,
    IReadOnlyList<KeyValuePair<string, string>> Arguments, Func<Task<MutationResult>> Apply);

/// <summary>Proposals waiting for the user's decision, keyed by id and filtered by story.</summary>
public sealed class PendingMutationQueue
{
    readonly ConcurrentDictionary<string, (long Order, PendingMutation Item)> _items = new(StringComparer.Ordinal);
    long _order;

    public PendingMutation Add(string storyTitle, string toolName, MutationResult preview,
        IReadOnlyList<KeyValuePair<string, string>> arguments, Func<Task<MutationResult>> apply)
    {
        var item = new PendingMutation(Guid.NewGuid().ToString("N"), storyTitle, toolName, preview, arguments, apply);
        _items[item.Id] = (Interlocked.Increment(ref _order), item);
        return item;
    }

    /// <summary>Removes and returns the proposal, but only when it belongs to <paramref name="storyTitle"/>.</summary>
    public bool TryTake(string storyTitle, string id, [NotNullWhen(true)] out PendingMutation? item)
    {
        item = null;
        if (!_items.TryGetValue(id, out var found) || !StoryTitle.SameTitle(found.Item.StoryTitle, storyTitle)) return false;
        if (!_items.TryRemove(id, out var taken)) return false;
        item = taken.Item;
        return true;
    }

    /// <summary>The story's proposals, oldest first.</summary>
    public IReadOnlyList<PendingMutation> For(string storyTitle) =>
        _items.Values.Where(p => StoryTitle.SameTitle(p.Item.StoryTitle, storyTitle)).OrderBy(p => p.Order).Select(p => p.Item).ToList();

    public void Clear(string storyTitle)
    {
        foreach (var p in For(storyTitle)) _items.TryRemove(p.Id, out _);
    }
}
