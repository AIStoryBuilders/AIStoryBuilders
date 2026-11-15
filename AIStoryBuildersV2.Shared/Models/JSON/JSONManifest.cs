#nullable disable
namespace AIStoryBuildersV2.Shared.Models.JSON;

public class JSONManifest
{
    public string Version { get; set; }
    public string Title { get; set; }
    public string Style { get; set; }
    public string Theme { get; set; }
    public string Synopsis { get; set; }
    public string ExportedDate { get; set; }

    /// <summary>Written by V2 (D6); V1 ignores it on import.</summary>
    public string WorldFacts { get; set; }
}
