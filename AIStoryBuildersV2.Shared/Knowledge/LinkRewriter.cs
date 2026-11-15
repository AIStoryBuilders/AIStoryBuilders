using System.Text;
using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>Rewrites links after a rename or move (§5.5).</summary>
public static class LinkRewriter
{
    /// <summary>Maps every page under <paramref name="oldPath"/> (a page or a folder) to its new path.</summary>
    public static Dictionary<string, string> MapMovedPages(IEnumerable<string> pages, string oldPath, string newPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            if (string.Equals(page, oldPath, StringComparison.OrdinalIgnoreCase)) map[page] = newPath;
            else if (page.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase)) map[page] = newPath + page[oldPath.Length..];
        }
        return map;
    }

    /// <summary>Old paths of the pages whose text may need rewriting: moved pages and pages that link to them.</summary>
    public static IReadOnlyList<string> AffectedPages(GraphSnapshot oldSnapshot, IReadOnlyDictionary<string, string> moved)
    {
        var result = new HashSet<string>(moved.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var target in moved.Keys)
        {
            if (oldSnapshot.Backlinks.TryGetValue(target, out var back))
                foreach (var link in back) result.Add(link.SourcePath);
        }
        return result.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Rewrites the links in one page. <paramref name="oldSourcePath"/> is the page's path before the move,
    /// and <paramref name="markdown"/> is its current text. Returns the text unchanged when nothing needs rewriting.
    /// </summary>
    public static string Rewrite(string markdown, string oldSourcePath, GraphSnapshot oldSnapshot,
                                 IReadOnlyDictionary<string, string> moved, PageDirectory newDirectory)
    {
        var newSourcePath = moved.TryGetValue(oldSourcePath, out var ns) ? ns : oldSourcePath;
        var parsed = new MarkdownPageParser().Parse(oldSourcePath, markdown);
        var edits = new List<(int Start, int Length, string Text)>();

        foreach (var link in parsed.Links)
        {
            if (link.Kind == LinkKind.Wiki)
            {
                var old = LinkResolver.ResolveWiki(oldSnapshot.Directory, oldSourcePath, link.RawTarget);
                if (old is null || old.Value.Step == ResolveStep.TitleOrAlias) continue;
                var target = moved.TryGetValue(old.Value.Path, out var t) ? t : old.Value.Path;

                var now = LinkResolver.ResolveWiki(newDirectory, newSourcePath, link.RawTarget, useTitles: false);
                if (now is not null && string.Equals(now.Value.Path, target, StringComparison.OrdinalIgnoreCase)) continue;

                var newTarget = WikiTargetFor(target, newSourcePath, newDirectory, preferPath: link.RawTarget.Contains('/'));
                edits.Add((link.SpanStart, link.SpanLength, BuildWiki(newTarget, link.Heading, link.Alias)));
            }
            else if (link.UrlStart >= 0)
            {
                var old = LinkResolver.ResolveMarkdown(oldSnapshot.Directory, oldSourcePath, link.RawTarget);
                if (old is null) continue;
                var target = moved.TryGetValue(old, out var t) ? t : old;
                var now = LinkResolver.ResolveMarkdown(newDirectory, newSourcePath, link.RawTarget);
                if (now is not null && string.Equals(now, target, StringComparison.OrdinalIgnoreCase)) continue;

                var fragment = link.RawTarget.IndexOf('#') is var h and >= 0 ? link.RawTarget[h..] : "";
                edits.Add((link.UrlStart, link.UrlLength, LinkResolver.RelativeUrl(newSourcePath, target) + fragment));
            }
        }

        if (edits.Count == 0) return markdown;

        // Apply from the end backwards so earlier spans stay valid.
        var sb = new StringBuilder(markdown);
        foreach (var (start, length, text) in edits.OrderByDescending(e => e.Start))
        {
            if (start < 0 || start + length > sb.Length) continue;
            sb.Remove(start, length).Insert(start, text);
        }
        return sb.ToString();
    }

    static string WikiTargetFor(string targetPath, string sourcePath, PageDirectory dir, bool preferPath)
    {
        var withoutExt = KnowledgePath.StripExtension(targetPath);
        if (preferPath && withoutExt.Contains('/')) return withoutExt;
        var bare = withoutExt.Contains('/') ? withoutExt[(withoutExt.LastIndexOf('/') + 1)..] : withoutExt;
        var resolved = LinkResolver.ResolveWiki(dir, sourcePath, bare, useTitles: false);
        return resolved is not null && string.Equals(resolved.Value.Path, targetPath, StringComparison.OrdinalIgnoreCase)
            && (resolved.Value.Step == ResolveStep.SameFolder || dir.ByName(bare).Count == 1)
            ? bare
            : withoutExt;
    }

    static string BuildWiki(string target, string? heading, string? alias) =>
        "[[" + target + (heading is null ? "" : "#" + heading) + (alias is null ? "" : "|" + alias) + "]]";
}
