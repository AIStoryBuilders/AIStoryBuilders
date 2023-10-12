namespace AIStoryBuilders.Models.JSON
{

    public class JSONStory
    {
        public Locations[] locations { get; set; }
        public Timelines[] timelines { get; set; }
        public Character[] characters { get; set; }
    }

    public class Locations
    {
        public string name { get; set; }
        public string[] descriptions { get; set; }
    }

    public class Timelines
    {
        public string name { get; set; }
        public string description { get; set; }
    }

    public class Character
    {
        public string name { get; set; }
        public Descriptions[] descriptions { get; set; }
    }

    public class Descriptions
    {
        public string description_type { get; set; }
        public string[] _enum { get; set; }
        public string description { get; set; }
        public string timeline_name { get; set; }
    }
}