#nullable disable
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Models.JSON;
using Newtonsoft.Json;

namespace AIStoryBuildersV2.Shared.Stories;
/// <summary>
/// Assembles a JSONMasterStory and trims context sections to fit within
/// the model's token budget.
/// </summary>
public class MasterStoryBuilder
{
    private readonly string _modelId;
    private readonly int _maxPromptTokens;

    public MasterStoryBuilder(string modelId)
    {
        _modelId = modelId;
        _maxPromptTokens = TokenEstimator.GetMaxPromptTokens(modelId);
    }

    /// <summary>
    /// Trim the Master Story's context sections to fit within the token budget.
    /// Uses relevance-ranked trimming: paragraphs from both Previous and Related
    /// are combined, sorted by relevance_score, and lowest-scoring items are
    /// dropped first until the total fits within the token budget.
    /// </summary>
    public JSONMasterStory TrimToFit(JSONMasterStory story, string systemPrompt, string userTemplate)
    {
        int baseTokens = EstimateBaseTokens(story, systemPrompt, userTemplate);
        int remainingBudget = _maxPromptTokens - baseTokens;
        if (remainingBudget < 0) remainingBudget = 0;

        // D15, plan §5.8: knowledge base notes are the first thing dropped when the whole prompt does not fit.
        if (!string.IsNullOrEmpty(story.KnowledgeBaseNotes))
        {
            var notesTokens = TokenEstimator.EstimateTokens(story.KnowledgeBaseNotes);
            var paragraphTokens = (story.PreviousParagraphs ?? new()).Concat(story.RelatedParagraphs ?? new())
                .Sum(p => TokenEstimator.EstimateTokens(JsonConvert.SerializeObject(p)));
            if (notesTokens + paragraphTokens > remainingBudget)
                story.KnowledgeBaseNotes = null;
            else
                remainingBudget -= notesTokens;
        }

        // Combine all trimmable paragraphs with source tracking
        var trimmable = new List<(JSONParagraphs Paragraph, string Source, int OriginalIndex)>();

        if (story.PreviousParagraphs != null)
        {
            for (int i = 0; i < story.PreviousParagraphs.Count; i++)
                trimmable.Add((story.PreviousParagraphs[i], "Previous", i));
        }
        if (story.RelatedParagraphs != null)
        {
            for (int i = 0; i < story.RelatedParagraphs.Count; i++)
                trimmable.Add((story.RelatedParagraphs[i], "Related", i));
        }

        // Sort by relevance_score descending — highest relevance kept first
        trimmable.Sort((a, b) => b.Paragraph.relevance_score.CompareTo(a.Paragraph.relevance_score));

        var kept = new List<(JSONParagraphs Paragraph, string Source, int OriginalIndex)>();

        foreach (var item in trimmable)
        {
            var tokenCost = TokenEstimator.EstimateTokens(
                JsonConvert.SerializeObject(item.Paragraph));
            if (tokenCost <= remainingBudget)
            {
                kept.Add(item);
                remainingBudget -= tokenCost;
            }
        }

        // Rebuild Previous and Related lists preserving original order
        story.PreviousParagraphs = kept
            .Where(k => k.Source == "Previous")
            .OrderBy(k => k.OriginalIndex)
            .Select(k => k.Paragraph)
            .ToList();

        story.RelatedParagraphs = kept
            .Where(k => k.Source == "Related")
            .OrderBy(k => k.OriginalIndex)
            .Select(k => k.Paragraph)
            .ToList();

        return story;
    }

    private int EstimateBaseTokens(JSONMasterStory story, string systemPrompt, string userTemplate)
    {
        var currentChapterMetadata = story.CurrentChapter is null
            ? null
            : new
            {
                story.CurrentChapter.chapter_name,
                story.CurrentChapter.chapter_synopsis
            };
        var fixedContent = string.Join("\n",
            systemPrompt,
            story.StoryTitle ?? "",
            story.StoryStyle ?? "",
            story.StorySynopsis ?? "",
            story.SystemMessage ?? "",
            string.Join("\n", story.WorldFacts ?? new List<string>()),
            story.TimelineSummary ?? "",
            JsonConvert.SerializeObject(currentChapterMetadata),
            JsonConvert.SerializeObject(story.CurrentLocation),
            JsonConvert.SerializeObject(story.CharacterList),
            JsonConvert.SerializeObject(story.CurrentParagraph));

        return TokenEstimator.EstimateTokens(fixedContent);
    }
}
