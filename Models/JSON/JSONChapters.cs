namespace AIStoryBuilders.Models.JSON
{
    public class JSONChapters
    {
        public JSONChapter[] chapter { get; set; }
    }
    public class JSONChapter
    {
        public string chapter_name { get; set; }
        public string chapter_synopsis { get; set; }
        public JSONParagraphs[] paragraphs { get; set; }
    }

    public class JSONParagraphs
    {
        public int sequence { get; set; }
        public string contents { get; set; }
        public string location_name { get; set; }
        public string timeline_name { get; set; }
        public string[] character_names { get; set; }
    }
}
