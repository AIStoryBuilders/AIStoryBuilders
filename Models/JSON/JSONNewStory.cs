namespace AIStoryBuilders.Models.JSON
{
    public class JSONNewStory
    {
        public Character[] characters { get; set; }
        public Location[] locations { get; set; }
        public string[] timelines { get; set; }
        public string firstparagraph { get; set; }
    }

    public class Character
    {
        public string name { get; set; }
        public string[] descriptions { get; set; }
    }

    public class Location
    {
        public string name { get; set; }
        public string[] descriptions { get; set; }
    }
}
