using AIStoryBuilders.Models;

namespace AIStoryBuilders.Services;

public static class GraphState
{
    public static StoryGraph Current { get; set; }
    public static Story CurrentStory { get; set; }
}
