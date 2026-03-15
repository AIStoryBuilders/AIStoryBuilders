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
    /// Priority order (trim first → last):
    ///   1. RelatedParagraphs
    ///   2. PreviousParagraphs (keep most recent; trim oldest first)
    ///   3. CharacterList (never trimmed)
    ///   4. System/Title/Style/Synopsis/Chapter (never trimmed)
    /// </summary>
    public JSONMasterStory TrimToFit(JSONMasterStory story, string systemPrompt, string userTemplate)
    {
        int baseTokens = EstimateBaseTokens(story, systemPrompt, userTemplate);
        int remainingBudget = _maxPromptTokens - baseTokens;
        if (remainingBudget < 0) remainingBudget = 0;

        story.RelatedParagraphs = TrimParagraphList(
            story.RelatedParagraphs, ref remainingBudget);

        story.PreviousParagraphs = TrimParagraphList(
            story.PreviousParagraphs, ref remainingBudget, keepNewest: true);

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
            JsonConvert.SerializeObject(story.CurrentChapter),
            JsonConvert.SerializeObject(story.CurrentLocation),
            JsonConvert.SerializeObject(story.CharacterList),
            JsonConvert.SerializeObject(story.CurrentParagraph));

        return TokenEstimator.EstimateTokens(fixedContent);
    }

    private static List<JSONParagraphs> TrimParagraphList(
        List<JSONParagraphs> paragraphs,
        ref int remainingBudget,
        bool keepNewest = false)
    {
        if (paragraphs == null || paragraphs.Count == 0)
            return new List<JSONParagraphs>();

        var result = new List<JSONParagraphs>();
        var ordered = keepNewest
            ? paragraphs.AsEnumerable().Reverse()
            : paragraphs.AsEnumerable();

        foreach (var p in ordered)
        {
            var tokenCost = TokenEstimator.EstimateTokens(
                JsonConvert.SerializeObject(p));
            if (tokenCost <= remainingBudget)
            {
                result.Add(p);
                remainingBudget -= tokenCost;
            }
            else break;
        }

        if (keepNewest) result.Reverse();
        return result;
    }
}
