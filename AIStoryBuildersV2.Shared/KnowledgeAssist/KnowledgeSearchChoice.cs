using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Services;

namespace AIStoryBuildersV2.Shared.KnowledgeAssist;

/// <summary>
/// D16: the "Search knowledgebase" checkbox. Checked when the story's KB has a page, otherwise unchecked and disabled; the
/// user's choice is kept per story and surface for the session only (no new story files).
/// </summary>
public sealed class KnowledgeSearchChoice(AppSession session, IStoryKnowledgeBaseProvider kbs)
{
    public const string Chat = "chat";
    public const string Paragraph = "paragraph";

    /// <returns>(Empty, Checked): an empty KB is never checked.</returns>
    public async Task<(bool Empty, bool Checked)> DefaultAsync(string storyTitle, string surface)
    {
        bool empty;
        var kb = await kbs.OpenAsync(storyTitle);
        try { empty = await kb.IsEmptyAsync(); }
        finally { await kbs.CloseAsync(storyTitle); }

        if (empty) return (true, false);
        lock (session.SearchKnowledgeBase)
            return (false, !session.SearchKnowledgeBase.TryGetValue((storyTitle, surface), out var remembered) || remembered);
    }

    public void Remember(string storyTitle, string surface, bool value)
    {
        lock (session.SearchKnowledgeBase) session.SearchKnowledgeBase[(storyTitle, surface)] = value;
    }
}
