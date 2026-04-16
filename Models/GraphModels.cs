namespace AIStoryBuilders.Models;

public enum NodeType
{
    Character,
    Location,
    Timeline,
    Chapter,
    Paragraph,
    Attribute
}

public class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public NodeType Type { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
}

public class GraphEdge
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new();
}

public class StoryGraph
{
    public string StoryTitle { get; set; } = string.Empty;
    public List<GraphNode> Nodes { get; set; } = new();
    public List<GraphEdge> Edges { get; set; } = new();
}
