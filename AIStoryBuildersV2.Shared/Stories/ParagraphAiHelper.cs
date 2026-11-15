using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.KnowledgeAssist;
using Character = AIStoryBuildersV2.Shared.Models.Character;

namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>
/// The paragraph editor's Generate pipeline (UI-Behavior-Spec §15.2, plan §7.5): CreateMasterStory with V1's arguments,
/// then, when "Search knowledgebase" is checked, KB retrieval (D15), then WriteParagraph with the chosen model.
/// </summary>
public class ParagraphAiHelper(AIStoryBuildersService svc, OrchestratorMethods orchestrator, KnowledgeSearchChoice choice,
    IKnowledgeContextRetriever retriever)
{
    public const string Surface = KnowledgeSearchChoice.Paragraph;

    /// <summary>D16: (empty, checked). Checked when the KB has pages, otherwise unchecked and disabled; remembered per session.</summary>
    public virtual Task<(bool Empty, bool Checked)> KnowledgeBaseDefaultAsync(string storyTitle) => choice.DefaultAsync(storyTitle, Surface);

    public void RememberKnowledgeBaseChoice(string storyTitle, bool value) => choice.Remember(storyTitle, Surface, value);

    /// <summary>Writes the paragraph with AI and returns the new text; the caller keeps the old text for Undo.</summary>
    public virtual async Task<string> GenerateAsync(Story story, Chapter chapter, Paragraph paragraph, IReadOnlyList<Character> storyCharacters,
        AIPrompt prompt, Action<string> warn, CancellationToken ct = default)
    {
        var before = svc.GetParagraphs(chapter).Where(p => p.Sequence < paragraph.Sequence).ToList();
        var names = paragraph.Characters.Select(c => c.CharacterName).ToHashSet(StringComparer.Ordinal);
        var characters = storyCharacters.Where(c => names.Contains(c.CharacterName)).ToList();
        chapter.Story = story;

        var master = await svc.CreateMasterStory(chapter, paragraph, characters, before, prompt);
        master.KnowledgeBaseNotes = await KnowledgeNotesAsync(story, chapter, paragraph, prompt, warn, ct);
        return await orchestrator.WriteParagraph(master, prompt, prompt.ModelId);
    }

    /// <summary>
    /// Plan §5.8: the query is the prompt text plus the chapter synopsis plus the paragraph's characters, location and
    /// timeline. A failed search warns and the paragraph is written without notes (D15).
    /// </summary>
    protected virtual async Task<string?> KnowledgeNotesAsync(Story story, Chapter chapter, Paragraph paragraph, AIPrompt prompt, Action<string> warn, CancellationToken ct)
    {
        if (!prompt.SearchKnowledgeBase) return null;
        var context = await retriever.RetrieveAsync(story.Title, BuildQuery(chapter, paragraph, prompt), prompt.ModelId, ct);
        if (context.Warning is not null) warn(context.Warning);
        return string.IsNullOrWhiteSpace(context.Notes) ? null : context.Notes;
    }

    internal static string BuildQuery(Chapter chapter, Paragraph paragraph, AIPrompt prompt)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(prompt.AIPromptText)) lines.Add(prompt.AIPromptText.Trim());
        if (!string.IsNullOrWhiteSpace(chapter.Synopsis)) lines.Add("Chapter synopsis: " + chapter.Synopsis.Trim());
        var characters = paragraph.Characters.Select(c => c.CharacterName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (characters.Count > 0) lines.Add("Characters: " + string.Join(", ", characters));
        if (!string.IsNullOrWhiteSpace(paragraph.Location?.LocationName)) lines.Add("Location: " + paragraph.Location.LocationName);
        if (!string.IsNullOrWhiteSpace(paragraph.Timeline?.TimelineName)) lines.Add("Timeline: " + paragraph.Timeline.TimelineName);
        if (lines.Count == 0) lines.Add("Write the next paragraph in the chapter.");
        return string.Join("\n", lines);
    }
}
