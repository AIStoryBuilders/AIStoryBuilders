namespace AIStoryBuilders.Models;

/// <summary>
/// Represents a cached list of models for a specific AI provider.
/// Serialized to JSON on the local file system.
/// </summary>
public class ModelCacheEntry
{
    public string ServiceType { get; set; } = "";
    public List<string> Models { get; set; } = new();
    public DateTimeOffset LastFetched { get; set; }
}
