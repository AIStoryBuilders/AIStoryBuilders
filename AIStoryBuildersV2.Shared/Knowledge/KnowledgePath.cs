using System.Diagnostics.CodeAnalysis;

namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>A validated, normalized, root-relative path with '/' separators. "" is the root.</summary>
public readonly record struct KnowledgePath
{
    public const int MaxLength = 240;
    public const string PageExtension = ".md";
    public const string TempPrefix = "~kgb-";

    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    static readonly char[] InvalidChars = ['<', '>', ':', '"', '|', '?', '*', '\\'];

    KnowledgePath(string value) => Value = value;

    public string Value { get; }

    public static KnowledgePath Root => new("");

    public bool IsRoot => string.IsNullOrEmpty(Value);

    public KnowledgePath Parent
    {
        get
        {
            var v = Value ?? "";
            var i = v.LastIndexOf('/');
            return i < 0 ? Root : new KnowledgePath(v[..i]);
        }
    }

    public string Name
    {
        get
        {
            var v = Value ?? "";
            var i = v.LastIndexOf('/');
            return i < 0 ? v : v[(i + 1)..];
        }
    }

    public bool IsUnder(KnowledgePath folder) =>
        folder.IsRoot ? !IsRoot : (Value ?? "").StartsWith(folder.Value + "/", StringComparison.OrdinalIgnoreCase);

    public KnowledgePath Combine(string name) => new(IsRoot ? name : Value + "/" + name);

    public override string ToString() => Value ?? "";

    public static KnowledgePath Parse(string raw, bool isPage) =>
        TryParse(raw, isPage, out var path, out var error) ? path : throw new InvalidKnowledgePathException(error!);

    public static bool TryParse(string? raw, bool isPage, out KnowledgePath path, [NotNullWhen(false)] out string? error)
    {
        path = Root;
        raw ??= "";
        raw = raw.Trim().Replace('\\', '/');

        if (raw.Length == 0)
        {
            if (isPage) { error = "A page name is required."; return false; }
            error = null;
            return true;
        }

        if (raw.StartsWith('/')) { error = "Paths must be relative to the knowledge root."; return false; }
        if (raw.Length >= 2 && raw[1] == ':') { error = "Drive letters are not allowed."; return false; }

        if (isPage)
        {
            if (raw.EndsWith(PageExtension, StringComparison.OrdinalIgnoreCase))
                raw = raw[..^PageExtension.Length];
            raw += PageExtension;
        }

        var segments = raw.Split('/');
        foreach (var segment in segments)
        {
            var segError = ValidateSegment(segment);
            if (segError is not null) { error = segError; return false; }
        }

        if (isPage && segments[^1].Length <= PageExtension.Length) { error = "A page name is required."; return false; }
        if (raw.Length > MaxLength) { error = $"The path is longer than {MaxLength} characters."; return false; }

        path = new KnowledgePath(raw);
        error = null;
        return true;
    }

    /// <summary>Validates a single file or folder name. Returns null when valid.</summary>
    public static string? ValidateSegment(string segment)
    {
        if (segment.Length == 0) return "Empty path segments are not allowed.";
        if (segment is "." or "..") return "'.' and '..' are not allowed.";
        if (segment.StartsWith('.')) return "Names can't start with '.'.";
        if (segment.StartsWith(TempPrefix, StringComparison.OrdinalIgnoreCase)) return $"Names can't start with '{TempPrefix}'.";
        if (segment.EndsWith('.') || segment.EndsWith(' ')) return "Names can't end with a dot or a space.";
        if (segment.StartsWith(' ')) return "Names can't start with a space.";
        foreach (var c in segment)
        {
            if (c < 32 || Array.IndexOf(InvalidChars, c) >= 0)
                return $"'{(c < 32 ? "control character" : c.ToString())}' is not allowed in names.";
        }
        var stem = segment.Split('.')[0];
        if (ReservedNames.Contains(stem)) return $"'{stem}' is a reserved name.";
        return null;
    }

    public static bool IsPagePath(string path) => path.EndsWith(PageExtension, StringComparison.OrdinalIgnoreCase);

    public static string StripExtension(string path) =>
        IsPagePath(path) ? path[..^PageExtension.Length] : path;
}
