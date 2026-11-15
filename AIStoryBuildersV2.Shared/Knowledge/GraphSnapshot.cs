using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Shared.Knowledge;

public sealed record GraphPage(ParsedPage Parsed, long ModifiedTicks)
{
    public PageInfo Info => new(Parsed.Path, Parsed.Title, Parsed.FrontMatter.Tags, Parsed.FrontMatter.Aliases, ModifiedTicks);
}

/// <summary>An immutable view of the graph. Every link is resolved against the full page set.</summary>
public sealed class GraphSnapshot
{
    public static readonly GraphSnapshot Empty = new(new Dictionary<string, GraphPage>(StringComparer.OrdinalIgnoreCase));

    public GraphSnapshot(IReadOnlyDictionary<string, GraphPage> pages)
    {
        Pages = pages;
        Directory = new PageDirectory(pages.Keys,
            pages.Values.Select(p => (p.Parsed.Path, p.Parsed.FrontMatter.Title, p.Parsed.FrontMatter.Aliases)));

        var outgoing = new Dictionary<string, IReadOnlyList<LinkRef>>(StringComparer.OrdinalIgnoreCase);
        var backlinks = new Dictionary<string, List<LinkRef>>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<LinkRef>();
        var count = 0;
        foreach (var page in pages.Values.OrderBy(p => p.Parsed.Path, StringComparer.Ordinal))
        {
            var list = new List<LinkRef>(page.Parsed.Links.Count);
            foreach (var link in page.Parsed.Links)
            {
                var resolved = link.Kind == LinkKind.Wiki
                    ? LinkResolver.ResolveWiki(Directory, page.Parsed.Path, link.RawTarget)?.Path
                    : LinkResolver.ResolveMarkdown(Directory, page.Parsed.Path, link.RawTarget);
                var r = new LinkRef(page.Parsed.Path, link.RawTarget, resolved, link.Kind, link.Line);
                list.Add(r);
                count++;
                if (resolved is null) unresolved.Add(r);
                else
                {
                    if (!backlinks.TryGetValue(resolved, out var b)) backlinks[resolved] = b = [];
                    b.Add(r);
                }
            }
            outgoing[page.Parsed.Path] = list;
        }

        Outgoing = outgoing;
        Backlinks = backlinks.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<LinkRef>)kv.Value, StringComparer.OrdinalIgnoreCase);
        Unresolved = unresolved;
        LinkCount = count;
    }

    public IReadOnlyDictionary<string, GraphPage> Pages { get; }
    public PageDirectory Directory { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<LinkRef>> Outgoing { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<LinkRef>> Backlinks { get; }
    public IReadOnlyList<LinkRef> Unresolved { get; }
    public int LinkCount { get; }

    public GraphStats Stats => new(Pages.Count, LinkCount, Unresolved.Count);

    public GraphSnapshot With(GraphPage page)
    {
        var pages = new Dictionary<string, GraphPage>(Pages, StringComparer.OrdinalIgnoreCase) { [page.Parsed.Path] = page };
        return new GraphSnapshot(pages);
    }

    public GraphSnapshot Without(string path)
    {
        var pages = new Dictionary<string, GraphPage>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in Pages)
        {
            if (string.Equals(k, path, StringComparison.OrdinalIgnoreCase) || k.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) continue;
            pages[k] = v;
        }
        return new GraphSnapshot(pages);
    }
}
