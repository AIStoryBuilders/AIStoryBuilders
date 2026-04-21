using AIStoryBuilders.Models;

namespace AIStoryBuilders.Services;

public static class GraphState
{
    public static StoryGraph Current { get; set; }
    public static Story CurrentStory { get; set; }

    /// <summary>
    /// When true, <see cref="Current"/> is out of sync with underlying story data
    /// and must be rebuilt before being queried. Set to true after any mutation;
    /// cleared after a successful rebuild.
    /// </summary>
    public static bool IsDirty { get; set; } = true;
}
