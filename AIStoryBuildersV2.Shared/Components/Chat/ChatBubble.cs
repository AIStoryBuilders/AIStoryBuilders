namespace AIStoryBuildersV2.Shared.Components.Chat;

/// <summary>
/// One bubble in the Story Chat tab. The list lives in <c>StoryWorkspace</c>, so it survives tab switches and is dropped when
/// the workspace closes (UI-Behavior-Spec §16, History).
/// </summary>
public sealed class ChatBubble
{
    public bool IsUser { get; init; }
    public string Text { get; set; } = "";

    /// <summary>The provisional AI bubble before the first chunk arrives.</summary>
    public bool Thinking { get; set; }

    /// <summary>Set on an error bubble: the user text that Retry sends again.</summary>
    public string? RetryText { get; init; }
}
