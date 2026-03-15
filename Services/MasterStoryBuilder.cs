using AIStoryBuilders.AI;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;

namespace AIStoryBuilders.Services;

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
        var fixedContent = string.Join("\n",
            systemPrompt,
            story.StoryTitle ?? "",
            story.StoryStyle ?? "",
            story.StorySynopsis ?? "",
            story.SystemMessage ?? "",
            string.Join("\n", story.WorldFacts ?? new List<string>()),
            JsonConvert.SerializeObject(story.CurrentChapter),
            JsonConvert.SerializeObject(story.CurrentLocation),
            JsonConvert.SerializeObject(story.CharacterList),
            JsonConvert.SerializeObject(story.CurrentParagraph));

        return TokenEstimator.EstimateTokens(fixedContent);
    }
}
