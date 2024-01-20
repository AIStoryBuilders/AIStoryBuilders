namespace AIStoryBuilders.Models.JSON
{
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
    }
}
