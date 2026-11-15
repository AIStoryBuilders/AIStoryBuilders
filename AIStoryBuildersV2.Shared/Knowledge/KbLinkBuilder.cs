namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>
/// Page links for one story's knowledge base (D13). Replaces KGB's static <c>SafeMarkdown.PageHref</c> and
/// <c>CreateHref</c>, which assumed KGB's own <c>/</c> route. Links are relative to the base href.
/// </summary>
public sealed class KbLinkBuilder(string storyTitle)
{
    public string StoryTitle { get; } = storyTitle;

    public string PageHref(string path) =>
        "story/" + Uri.EscapeDataString(StoryTitle) + "?tab=kb&p=" + Uri.EscapeDataString(path);

    /// <summary>Opening this link creates the page instead of opening it (UI-Behavior-Spec §17.5).</summary>
    public string CreateHref(string path) => PageHref(path) + "&create=1";
}
