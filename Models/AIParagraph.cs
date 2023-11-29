namespace AIStoryBuilders.Models
{
    public class AIParagraph
    {
        public int sequence { get; set; }
        public string contents { get; set; }
        public string location_name { get; set; }
        public string timeline_name { get; set; }
        public string vectors { get; set; }
        public string[] character_names { get; set; }
    }
}
