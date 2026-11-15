namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>
/// Story title rules (D29). A title is a folder name, a storage key and a route segment, so it is validated when a
/// story is created, renamed or imported. Existing V1 folders are grandfathered: see <see cref="IsSafeExistingName"/>.
/// </summary>
public static class StoryTitle
{
    public const int MaxLength = 100;

    static readonly char[] Forbidden = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '#'];

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <returns>null when the title is valid, otherwise the message to show (UI-Behavior-Spec §3).</returns>
    public static string? Validate(string? title)
    {
        if (string.IsNullOrEmpty(title)) return "Story Title is required";
        if (title.Length > MaxLength) return "Story Title must be 100 characters or fewer.";
        if (title.IndexOfAny(Forbidden) >= 0 || title.Any(char.IsControl)) return "Story Title cannot contain / \\ : * ? \" < > | or #";
        if (title.Contains("..", StringComparison.Ordinal)) return "Story Title cannot contain '..'.";
        if (title[0] is ' ' or '.' || title[^1] is ' ' or '.') return "Story Title cannot start or end with a space or a dot.";
        var stem = title.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem)) return $"'{title}' is a reserved name. Choose another title.";
        return null;
    }

    /// <summary>The only check applied to an existing V1 story folder name (D29): it must not escape the stories root.</summary>
    public static bool IsSafeExistingName(string? title) =>
        !string.IsNullOrWhiteSpace(title)
        && title.IndexOfAny(['/', '\\']) < 0
        && !title.Contains("..", StringComparison.Ordinal);

    /// <summary>Uniqueness ignores case (D29).</summary>
    public static bool SameTitle(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The route to a story's workspace (D20).</summary>
    public static string WorkspaceHref(string title, string? tab = null) =>
        "story/" + Uri.EscapeDataString(title) + (tab is null ? "" : "?tab=" + tab);
}
