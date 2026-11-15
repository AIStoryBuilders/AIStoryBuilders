using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Shared.Knowledge;

public enum ResolveStep { Path = 1, SameFolder = 2, Anywhere = 3, TitleOrAlias = 4 }

public readonly record struct Resolution(string Path, ResolveStep Step);

/// <summary>A lookup over a set of page paths plus their titles and aliases.</summary>
public sealed class PageDirectory
{
    readonly Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> _byName = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> _byTitle = new(StringComparer.OrdinalIgnoreCase);

    public PageDirectory(IEnumerable<string> paths, IEnumerable<(string Path, string? Title, IReadOnlyList<string> Aliases)>? titles = null)
    {
        foreach (var path in paths)
        {
            _byPath[path] = path;
            var name = KnowledgePath.StripExtension(path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path);
            Add(_byName, name, path);
        }
        if (titles is null) return;
        foreach (var (path, title, aliases) in titles)
        {
            if (!string.IsNullOrWhiteSpace(title)) Add(_byTitle, title, path);
            foreach (var alias in aliases) Add(_byTitle, alias, path);
        }
        foreach (var list in _byName.Values.Concat(_byTitle.Values))
            list.Sort(ComparePaths);
    }

    static void Add(Dictionary<string, List<string>> map, string key, string path)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        if (!list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path);
    }

    /// <summary>Shortest path first, then ordinal order.</summary>
    public static int ComparePaths(string a, string b)
    {
        var c = a.Length.CompareTo(b.Length);
        return c != 0 ? c : StringComparer.Ordinal.Compare(a, b);
    }

    public bool Contains(string path) => _byPath.ContainsKey(path);

    public string? Canonical(string path) => _byPath.TryGetValue(path, out var p) ? p : null;

    public IReadOnlyList<string> ByName(string name) => _byName.TryGetValue(name, out var l) ? l : [];

    public IReadOnlyList<string> ByTitle(string title) => _byTitle.TryGetValue(title, out var l) ? l : [];

    public IEnumerable<string> Paths => _byPath.Values;
}

/// <summary>Resolves wiki and Markdown link targets (§5.5).</summary>
public static class LinkResolver
{
    public static Resolution? ResolveWiki(PageDirectory dir, string sourcePath, string rawTarget, bool useTitles = true)
    {
        var target = rawTarget.Trim().Replace('\\', '/').Trim('/');
        if (target.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) target = target[..^3];
        if (target.Length == 0) return null;

        if (target.Contains('/'))
        {
            if (dir.Canonical(target + ".md") is { } p) return new Resolution(p, ResolveStep.Path);
        }
        else
        {
            var folder = FolderOf(sourcePath);
            var candidate = folder.Length == 0 ? target + ".md" : folder + "/" + target + ".md";
            if (dir.Canonical(candidate) is { } same) return new Resolution(same, ResolveStep.SameFolder);
            if (dir.ByName(target) is [var first, ..]) return new Resolution(first, ResolveStep.Anywhere);
        }

        if (useTitles && dir.ByTitle(target) is [var titled, ..]) return new Resolution(titled, ResolveStep.TitleOrAlias);
        return null;
    }

    public static string? ResolveMarkdown(PageDirectory dir, string sourcePath, string url)
    {
        var target = RelativeTarget(sourcePath, url);
        return target is null ? null : dir.Canonical(target);
    }

    /// <summary>The root-relative path a relative Markdown link points to, or null when it is external or invalid.</summary>
    public static string? RelativeTarget(string sourcePath, string url)
    {
        if (string.IsNullOrWhiteSpace(url) || MarkdownPageParser.IsExternal(url)) return null;
        var hash = url.IndexOf('#');
        if (hash >= 0) url = url[..hash];
        var query = url.IndexOf('?');
        if (query >= 0) url = url[..query];
        try { url = Uri.UnescapeDataString(url); } catch (UriFormatException) { return null; }
        url = url.Replace('\\', '/');

        var parts = new List<string>();
        if (!url.StartsWith('/'))
        {
            var folder = FolderOf(sourcePath);
            if (folder.Length > 0) parts.AddRange(folder.Split('/'));
        }
        foreach (var seg in url.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(seg);
        }
        if (parts.Count == 0) return null;
        return string.Join('/', parts);
    }

    /// <summary>A relative URL from <paramref name="sourcePath"/>'s folder to <paramref name="targetPath"/>, with each segment escaped.</summary>
    public static string RelativeUrl(string sourcePath, string targetPath)
    {
        var from = FolderOf(sourcePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var to = targetPath.Split('/');
        var common = 0;
        while (common < from.Length && common < to.Length - 1 && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
            common++;
        var segments = Enumerable.Repeat("..", from.Length - common).Concat(to.Skip(common).Select(Uri.EscapeDataString));
        return string.Join('/', segments);
    }

    public static string FolderOf(string path)
    {
        var i = path.LastIndexOf('/');
        return i < 0 ? "" : path[..i];
    }
}
