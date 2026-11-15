using System.Text.RegularExpressions;
using AIStoryBuildersV2.Shared.Models;

namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>How the Manuscript Editor names a chapter (UI-Behavior-Spec §14.1). V1 chapters have only a name and a synopsis.</summary>
public static partial class ChapterDisplay
{
    public const int MaxTitleLength = 60;

    /// <summary>
    /// The synopsis's first sentence, cut to 60 characters, or the chapter name when the synopsis is empty. A leading
    /// "#Beat 1 - " marker, which V1's AI writes into synopses, is dropped (plan §14, decided by implementer).
    /// </summary>
    public static string Title(Chapter chapter)
    {
        var synopsis = BeatMarker().Replace((chapter.Synopsis ?? "").Trim(), "").Trim();
        if (synopsis.Length == 0) return chapter.ChapterName;
        var end = synopsis.IndexOfAny(['.', '!', '?']);
        var sentence = end > 0 ? synopsis[..(end + 1)] : synopsis;
        // The beats run together without a space ("…midday.#Beat 2 - …"), so a sentence may still end at the next marker.
        var next = sentence.IndexOf('#');
        if (next > 0) sentence = sentence[..next].TrimEnd();
        return sentence.Length > MaxTitleLength ? sentence[..MaxTitleLength].TrimEnd() + "…" : sentence;
    }

    /// <summary>The chapter dropdown text: "{ChapterName}: {title}", or the name alone.</summary>
    public static string DropdownText(Chapter chapter) =>
        string.IsNullOrWhiteSpace(chapter.Synopsis) ? chapter.ChapterName : $"{chapter.ChapterName}: {Title(chapter)}";

    [GeneratedRegex(@"^#\s*Beat\s*\d+\s*[-–—:]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex BeatMarker();
}
