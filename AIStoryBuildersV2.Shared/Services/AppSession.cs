namespace AIStoryBuildersV2.Shared.Services;

/// <summary>Per-app-start UI state (a singleton, not static state).</summary>
public sealed class AppSession
{
    /// <summary>The first-run redirect to Settings happens once per app start (UI-Behavior-Spec §1).</summary>
    public bool FirstRunRedirectDone { get; set; }

    /// <summary>The "Search knowledgebase" choice per story and surface, kept for the session only (D16).</summary>
    public Dictionary<(string Story, string Surface), bool> SearchKnowledgeBase { get; } = [];
}
