#nullable disable
namespace AIStoryBuildersV2.Shared.Models;
public readonly record struct ParagraphVectorEntry(
    string Id,
    string Content,
    float[] Vectors,
    string TimelineName);
