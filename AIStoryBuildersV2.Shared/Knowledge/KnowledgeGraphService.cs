using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>Holds an immutable <see cref="GraphSnapshot"/>; mutations are serialized.</summary>
public sealed class KnowledgeGraphService(IKnowledgeStore store) : IKnowledgeGraphIndex
{
    readonly MarkdownPageParser _parser = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    volatile GraphSnapshot _snapshot = GraphSnapshot.Empty;
    volatile bool _built;

    public GraphSnapshot Snapshot => _snapshot;

    public bool IsBuilt => _built;

    public void Invalidate() => _built = false;

    public async Task<GraphStats> RebuildAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var pages = new Dictionary<string, GraphPage>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in await store.GetAllPagePathsAsync(ct))
            {
                try
                {
                    var content = await store.ReadPageAsync(path, ct);
                    pages[content.Path] = new GraphPage(_parser.Parse(content.Path, content.Markdown), content.ModifiedTicks);
                }
                catch (KnowledgeException) { /* skip unreadable pages */ }
            }
            _snapshot = new GraphSnapshot(pages);
            _built = true;
            return _snapshot.Stats;
        }
        finally { _gate.Release(); }
    }

    async Task<GraphSnapshot> EnsureAsync(CancellationToken ct)
    {
        if (!_built) await RebuildAsync(ct);
        return _snapshot;
    }

    public async Task UpdatePageAsync(string path, CancellationToken ct = default)
    {
        if (!_built) { await RebuildAsync(ct); return; }
        await _gate.WaitAsync(ct);
        try
        {
            PageContent content;
            try { content = await store.ReadPageAsync(path, ct); }
            catch (KnowledgeNotFoundException)
            {
                _snapshot = _snapshot.Without(path);
                return;
            }
            _snapshot = _snapshot.Without(content.Path).With(new GraphPage(_parser.Parse(content.Path, content.Markdown), content.ModifiedTicks));
        }
        finally { _gate.Release(); }
    }

    /// <summary>Updates the graph from text already in hand (avoids a re-read).</summary>
    public async Task UpdatePageAsync(string path, string markdown, long ticks, CancellationToken ct = default)
    {
        if (!_built) { await RebuildAsync(ct); return; }
        await _gate.WaitAsync(ct);
        try { _snapshot = _snapshot.Without(path).With(new GraphPage(_parser.Parse(path, markdown), ticks)); }
        finally { _gate.Release(); }
    }

    public async Task RemovePathAsync(string path, CancellationToken ct = default)
    {
        if (!_built) return;
        await _gate.WaitAsync(ct);
        try { _snapshot = _snapshot.Without(path); }
        finally { _gate.Release(); }
    }

    public async Task<GraphStats> GetStatsAsync(CancellationToken ct = default) => (await EnsureAsync(ct)).Stats;

    public async Task<PageLinks> GetLinksAsync(string path, CancellationToken ct = default)
    {
        var s = await EnsureAsync(ct);
        var outgoing = s.Outgoing.TryGetValue(path, out var o) ? o : [];
        var back = s.Backlinks.TryGetValue(path, out var b) ? b.Where(l => !string.Equals(l.SourcePath, path, StringComparison.OrdinalIgnoreCase)).ToList() : [];
        return new PageLinks(outgoing, back);
    }

    public async Task<IReadOnlyList<LinkRef>> GetUnresolvedLinksAsync(CancellationToken ct = default) => (await EnsureAsync(ct)).Unresolved;

    public async Task<PageInfo?> GetPageInfoAsync(string path, CancellationToken ct = default)
    {
        var s = await EnsureAsync(ct);
        return s.Pages.TryGetValue(path, out var p) ? p.Info : null;
    }

    public async Task<string?> ResolveAsync(string sourcePath, string rawTarget, CancellationToken ct = default)
    {
        var s = await EnsureAsync(ct);
        return LinkResolver.ResolveWiki(s.Directory, sourcePath, rawTarget)?.Path;
    }

    public async Task<Neighborhood> GetNeighborhoodAsync(string path, int depth = 1, CancellationToken ct = default)
    {
        var s = await EnsureAsync(ct);
        depth = Math.Clamp(depth, 1, 3);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links = new List<LinkRef>();
        if (s.Pages.ContainsKey(path)) seen.Add(path);
        var frontier = new List<string> { path };
        for (var d = 0; d < depth && frontier.Count > 0; d++)
        {
            var next = new List<string>();
            foreach (var p in frontier)
            {
                IEnumerable<LinkRef> edges = (s.Outgoing.TryGetValue(p, out var o) ? o : [])
                    .Concat(s.Backlinks.TryGetValue(p, out var b) ? b : []);
                foreach (var e in edges.Where(e => e.ResolvedPath is not null))
                {
                    if (!links.Contains(e)) links.Add(e);
                    foreach (var other in new[] { e.SourcePath, e.ResolvedPath! })
                    {
                        if (seen.Add(other)) next.Add(other);
                    }
                }
            }
            frontier = next;
        }
        var pages = seen.Where(s.Pages.ContainsKey).Select(p => s.Pages[p].Info).ToList();
        return new Neighborhood(pages, links);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int top = 10, CancellationToken ct = default)
    {
        var s = await EnsureAsync(ct);
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('"', '\'')).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (terms.Count == 0) return [];

        var hits = new List<(SearchHit Hit, long Ticks)>();
        foreach (var page in s.Pages.Values)
        {
            var p = page.Parsed;
            double score = 0;
            foreach (var term in terms)
            {
                if (p.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 10;
                if (p.FrontMatter.Aliases.Any(a => a.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 6;
                if (p.FrontMatter.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 4;
                if (KnowledgePath.StripExtension(p.Path).Split('/').Any(seg => seg.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 3;
                score += Math.Min(5, CountOccurrences(p.PlainText, term));
            }
            if (score > 0) hits.Add((new SearchHit(p.Path, p.Title, Snippet(p.PlainText, terms), score), page.ModifiedTicks));
        }

        return hits.OrderByDescending(h => h.Hit.Score).ThenByDescending(h => h.Ticks).ThenBy(h => h.Hit.Path, StringComparer.Ordinal)
                   .Take(Math.Max(1, top)).Select(h => h.Hit).ToList();
    }

    static int CountOccurrences(string text, string term)
    {
        var count = 0;
        for (var i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); i >= 0 && count < 5; i = text.IndexOf(term, i + term.Length, StringComparison.OrdinalIgnoreCase))
            count++;
        return count;
    }

    internal static string Snippet(string text, IReadOnlyList<string> terms, int length = 160)
    {
        if (text.Length <= length) return text;
        var first = terms.Select(t => text.IndexOf(t, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, first - length / 3);
        if (start + length > text.Length) start = text.Length - length;
        var snippet = text.Substring(start, length).Trim();
        return (start > 0 ? "…" : "") + snippet + (start + length < text.Length ? "…" : "");
    }
}
