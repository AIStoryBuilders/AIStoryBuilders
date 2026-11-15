#nullable disable
namespace AIStoryBuildersV2.Shared.Models.JSON;

public class JSONMasterStory
{
    public string StoryTitle { get; set; }
    public string StorySynopsis { get; set; }
    public string StoryStyle { get; set; }
    public string SystemMessage { get; set; }
    public List<Character> CharacterList { get; set; }
    public List<JSONParagraphs> PreviousParagraphs { get; set; }
    public List<JSONParagraphs> RelatedParagraphs { get; set; }
    public Locations CurrentLocation { get; set; }
    public JSONChapter CurrentChapter { get; set; }
    public JSONParagraphs CurrentParagraph { get; set; }
    public List<string> WorldFacts { get; set; }
    public string TimelineSummary { get; set; }

    /// <summary>NEW in V2 (D15): cited notes from the story's knowledge base. Trimmed first by the token budget.</summary>
    public string KnowledgeBaseNotes { get; set; }
}
