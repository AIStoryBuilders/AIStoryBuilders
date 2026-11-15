namespace AIStoryBuildersV2.Shared.Knowledge;

/// <summary>A node in the visual graph: a page, or a link target with no page yet (<see cref="Missing"/>).</summary>
public sealed record GraphNode(string Id, string Label, string? Path, string Group, bool Missing, int Degree)
{
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Radius => Math.Min(16, 5 + 2 * Math.Sqrt(Degree));
}

public sealed record GraphEdge(string From, string To, bool ToMissing);

/// <param name="HiddenFolders">Top-level folders to leave out ("" is the top level). The center page is always kept.</param>
public sealed record GraphViewOptions(string? Center = null, int Depth = 1, bool IncludeMissing = true, int MaxNodes = 400,
    IReadOnlyCollection<string>? HiddenFolders = null);

/// <summary>A top-level folder and how many of its pages the view could show, hidden or not.</summary>
public sealed record GraphFolder(string Name, int Pages);

/// <param name="Truncated">The whole graph had more pages than MaxNodes; only the best-connected ones are shown.</param>
public sealed record GraphView(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, bool Truncated, int TotalPages)
{
    public static readonly GraphView Empty = new([], [], false, 0);

    /// <summary>Every folder in scope, including hidden ones, so the filter can bring them back.</summary>
    public IReadOnlyList<GraphFolder> Folders { get; init; } = [];

    /// <summary>
    /// The area to show, at least <paramref name="minWidth"/> × <paramref name="minHeight"/> around the nodes' center, so a
    /// small graph keeps its natural size instead of being blown up to fill the window.
    /// </summary>
    public (double MinX, double MinY, double Width, double Height) Bounds(double padding = 40, double minWidth = 900, double minHeight = 560)
    {
        if (Nodes.Count == 0) return (-minWidth / 2, -minHeight / 2, minWidth, minHeight);
        var minX = Nodes.Min(n => n.X - n.Radius) - padding;
        var minY = Nodes.Min(n => n.Y - n.Radius) - padding;
        var maxX = Nodes.Max(n => n.X + n.Radius) + padding;
        // Room for the label under the lowest node.
        var maxY = Nodes.Max(n => n.Y + n.Radius) + padding + 12;
        var width = Math.Max(minWidth, maxX - minX);
        var height = Math.Max(minHeight, maxY - minY);
        return ((minX + maxX - width) / 2, (minY + maxY - height) / 2, width, height);
    }
}

/// <summary>Turns a <see cref="GraphSnapshot"/> into laid-out nodes and edges for the Graph page.</summary>
public static class GraphViewBuilder
{
    const string MissingPrefix = "?";

    public static GraphView Build(GraphSnapshot snapshot, GraphViewOptions options)
    {
        var center = options.Center is not null && snapshot.Pages.ContainsKey(options.Center)
            ? snapshot.Pages[options.Center].Parsed.Path
            : null;

        // Undirected adjacency between pages, plus each page's missing targets.
        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var missingOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var missingLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in snapshot.Pages.Keys) neighbors[path] = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, links) in snapshot.Outgoing)
        {
            foreach (var link in links)
            {
                if (link.ResolvedPath is { } target)
                {
                    if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase)) continue;
                    neighbors[source].Add(target);
                    neighbors[target].Add(source);
                }
                else if (MissingName(link.RawTarget) is { } name)
                {
                    var id = MissingPrefix + name.ToLowerInvariant();
                    missingLabel.TryAdd(id, name);
                    if (!missingOf.TryGetValue(source, out var set)) missingOf[source] = set = new(StringComparer.OrdinalIgnoreCase);
                    set.Add(id);
                }
            }
        }

        var hidden = new HashSet<string>(options.HiddenFolders ?? [], StringComparer.OrdinalIgnoreCase);
        bool Visible(string path) => !hidden.Contains(TopFolder(path)) || string.Equals(path, center, StringComparison.OrdinalIgnoreCase);

        // Pick the pages to show.
        HashSet<string> pages;
        List<string> inScope;
        var truncated = false;
        if (center is not null)
        {
            pages = new(StringComparer.OrdinalIgnoreCase) { center };
            var frontier = new List<string> { center };
            for (var d = 0; d < Math.Max(1, options.Depth) && frontier.Count > 0; d++)
            {
                var next = new List<string>();
                foreach (var p in frontier)
                    foreach (var n in neighbors[p])
                        if (pages.Add(n)) next.Add(n);
                frontier = next;
            }
            inScope = [.. pages];
            pages.RemoveWhere(p => !Visible(p));
        }
        else
        {
            inScope = [.. snapshot.Pages.Keys];
            var all = inScope.Where(Visible).OrderByDescending(p => neighbors[p].Count).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            truncated = all.Count > options.MaxNodes;
            pages = new(all.Take(options.MaxNodes), StringComparer.OrdinalIgnoreCase);
        }
        var folders = inScope.GroupBy(TopFolder, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GraphFolder(g.Key, g.Count()))
            .OrderBy(f => f.Name.Length == 0 ? 0 : 1).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Edges: one per connected pair, in a stable order.
        var edges = new List<GraphEdge>();
        var seen = new HashSet<(string, string)>();
        foreach (var source in pages.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var target in neighbors[source].Where(pages.Contains).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var key = string.Compare(source, target, StringComparison.OrdinalIgnoreCase) < 0
                    ? (source.ToLowerInvariant(), target.ToLowerInvariant())
                    : (target.ToLowerInvariant(), source.ToLowerInvariant());
                if (seen.Add(key)) edges.Add(new GraphEdge(source, target, false));
            }
        }

        var missingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.IncludeMissing)
        {
            foreach (var source in pages.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (!missingOf.TryGetValue(source, out var set)) continue;
                foreach (var id in set.OrderBy(i => i, StringComparer.Ordinal))
                {
                    missingIds.Add(id);
                    edges.Add(new GraphEdge(source, id, true));
                }
            }
        }

        var degree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in edges)
        {
            degree[e.From] = degree.GetValueOrDefault(e.From) + 1;
            degree[e.To] = degree.GetValueOrDefault(e.To) + 1;
        }

        var nodes = new List<GraphNode>();
        foreach (var path in pages.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var page = snapshot.Pages[path].Parsed;
            nodes.Add(new GraphNode(page.Path, page.Title, page.Path, TopFolder(page.Path), false, degree.GetValueOrDefault(page.Path)));
        }
        foreach (var id in missingIds.OrderBy(i => i, StringComparer.Ordinal))
            nodes.Add(new GraphNode(id, missingLabel[id], null, "", true, degree.GetValueOrDefault(id)));

        var view = new GraphView(nodes, edges, truncated, snapshot.Pages.Count) { Folders = folders };
        ForceLayout.Run(view, center);
        return view;
    }

    /// <summary>The page name a missing link points at: no heading, alias text or extension. null for an empty target.</summary>
    internal static string? MissingName(string rawTarget)
    {
        var t = rawTarget;
        var cut = t.IndexOfAny(['#', '|']);
        if (cut >= 0) t = t[..cut];
        t = Uri.UnescapeDataString(t.Trim().Replace('\\', '/')).TrimStart('.', '/');
        if (t.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) t = t[..^3];
        return t.Length == 0 ? null : t;
    }

    static string TopFolder(string path)
    {
        var slash = path.IndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }
}

/// <summary>Finds nodes for the Graph page's search box.</summary>
public static class GraphSearch
{
    /// <summary>
    /// Nodes matching <paramref name="query"/>, best first: title starts with it, then title contains it, then the path
    /// or an alias does, then the page text does. Ties go to the better-connected node.
    /// </summary>
    public static IReadOnlyList<GraphNode> Find(GraphView view, GraphSnapshot snapshot, string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];
        var hits = new List<(GraphNode Node, int Rank)>();
        foreach (var n in view.Nodes)
        {
            var page = n.Path is not null && snapshot.Pages.TryGetValue(n.Path, out var p) ? p.Parsed : null;
            int rank;
            if (n.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase)) rank = 0;
            else if (n.Label.Contains(q, StringComparison.OrdinalIgnoreCase)) rank = 1;
            else if ((n.Path?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                     || (page?.FrontMatter.Aliases.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? false)) rank = 2;
            else if (page?.PlainText.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) rank = 3;
            else continue;
            hits.Add((n, rank));
        }
        return hits.OrderBy(h => h.Rank).ThenByDescending(h => h.Node.Degree).ThenBy(h => h.Node.Label, StringComparer.OrdinalIgnoreCase)
                   .Select(h => h.Node).ToList();
    }
}

/// <summary>
/// Fruchterman–Reingold layout with a spatial grid for repulsion, so it stays roughly linear in the node count.
/// Deterministic: the same graph always gets the same picture.
/// </summary>
public static class ForceLayout
{
    const double K = 70;          // Ideal edge length.
    const double Gravity = 0.04;  // Keeps separate clusters from drifting away.

    public static void Run(GraphView view, string? center = null, int iterations = 0)
    {
        var nodes = view.Nodes;
        var n = nodes.Count;
        if (n == 0) return;
        // Big graphs settle in fewer passes, and WebAssembly runs this on the UI thread.
        if (iterations <= 0) iterations = n <= 60 ? 300 : n <= 200 ? 160 : 90;

        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < n; i++) index[nodes[i].Id] = i;
        var edges = view.Edges
            .Select(e => (A: index.GetValueOrDefault(e.From, -1), B: index.GetValueOrDefault(e.To, -1)))
            .Where(e => e.A >= 0 && e.B >= 0)
            .ToArray();

        // Start on a golden-angle spiral (stable, no randomness); the center page starts in the middle.
        var x = new double[n];
        var y = new double[n];
        var order = Enumerable.Range(0, n).OrderBy(i => string.Equals(nodes[i].Id, center, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenByDescending(i => nodes[i].Degree).ToArray();
        for (var r = 0; r < n; r++)
        {
            var i = order[r];
            var radius = K * 0.6 * Math.Sqrt(r);
            var angle = r * 2.399963229728653;
            x[i] = radius * Math.Cos(angle);
            y[i] = radius * Math.Sin(angle);
        }

        var dx = new double[n];
        var dy = new double[n];
        var temperature = K * Math.Max(2, Math.Sqrt(n) / 2);
        var cooling = temperature / (iterations + 1);
        var cell = 2 * K;
        var grid = new Dictionary<(int, int), List<int>>();

        for (var it = 0; it < iterations; it++)
        {
            Array.Clear(dx);
            Array.Clear(dy);

            grid.Clear();
            for (var i = 0; i < n; i++)
            {
                var key = ((int)Math.Floor(x[i] / cell), (int)Math.Floor(y[i] / cell));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
                list.Add(i);
            }

            // Repulsion from nodes in the same and neighboring cells.
            for (var i = 0; i < n; i++)
            {
                var cx = (int)Math.Floor(x[i] / cell);
                var cy = (int)Math.Floor(y[i] / cell);
                for (var gx = cx - 1; gx <= cx + 1; gx++)
                for (var gy = cy - 1; gy <= cy + 1; gy++)
                {
                    if (!grid.TryGetValue((gx, gy), out var list)) continue;
                    foreach (var j in list)
                    {
                        if (j <= i) continue;
                        var ddx = x[i] - x[j];
                        var ddy = y[i] - y[j];
                        var dist2 = ddx * ddx + ddy * ddy;
                        if (dist2 < 0.01) { ddx = 0.1 * ((i - j) % 3 - 1 + 0.5); ddy = 0.1; dist2 = ddx * ddx + ddy * ddy; }
                        var dist = Math.Sqrt(dist2);
                        if (dist > cell) continue;
                        var force = K * K / dist;
                        var fx = ddx / dist * force;
                        var fy = ddy / dist * force;
                        dx[i] += fx; dy[i] += fy;
                        dx[j] -= fx; dy[j] -= fy;
                    }
                }
            }

            // Attraction along edges.
            foreach (var (a, b) in edges)
            {
                var ddx = x[a] - x[b];
                var ddy = y[a] - y[b];
                var dist = Math.Max(0.01, Math.Sqrt(ddx * ddx + ddy * ddy));
                var force = dist * dist / K;
                var fx = ddx / dist * force;
                var fy = ddy / dist * force;
                dx[a] -= fx; dy[a] -= fy;
                dx[b] += fx; dy[b] += fy;
            }

            // Gravity toward the origin, then move each node by at most the temperature.
            for (var i = 0; i < n; i++)
            {
                dx[i] -= Gravity * x[i] * K / 10;
                dy[i] -= Gravity * y[i] * K / 10;
                var len = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                if (len < 1e-9) continue;
                var step = Math.Min(len, temperature);
                x[i] += dx[i] / len * step;
                y[i] += dy[i] / len * step;
            }
            temperature = Math.Max(1, temperature - cooling);
        }

        // Keep the center page in the middle of the picture.
        var shiftX = 0.0;
        var shiftY = 0.0;
        if (center is not null && index.TryGetValue(center, out var c)) { shiftX = x[c]; shiftY = y[c]; }
        for (var i = 0; i < n; i++)
        {
            nodes[i].X = Math.Round(x[i] - shiftX, 1);
            nodes[i].Y = Math.Round(y[i] - shiftY, 1);
        }
    }
}
