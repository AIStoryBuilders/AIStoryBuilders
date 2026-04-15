using AIStoryBuilders.Models;

namespace AIStoryBuilders.Services;

public interface IGraphBuilder
{
    StoryGraph Build(Story story);
}

public class GraphBuilder : IGraphBuilder
{
    public StoryGraph Build(Story story)
    {
        var graph = new StoryGraph { StoryTitle = story.Title ?? "" };
        var nodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var edgeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Phase 1: Node Creation ─────────────────────────────

        // Characters
        var knownCharNames = new List<string>();
        foreach (var c in story.Character ?? new())
        {
            if (!IsValidEntity(c.CharacterName)) continue;
            var name = Normalize(c.CharacterName);
            knownCharNames.Add(name);
            var id = $"character:{name.ToLowerInvariant()}";
            if (nodes.ContainsKey(id)) continue;

            var role = "";
            var backstory = "";
            if (c.CharacterBackground != null && c.CharacterBackground.Count > 0)
            {
                role = c.CharacterBackground.FirstOrDefault()?.Type ?? "";
                backstory = string.Join("; ",
                    c.CharacterBackground.Select(bg => bg.Description ?? "").Where(d => d.Length > 0));
            }

            nodes[id] = new GraphNode
            {
                Id = id,
                Label = name,
                Type = NodeType.Character,
                Properties = new() { ["role"] = role, ["backstory"] = backstory }
            };
        }

        // Locations
        foreach (var loc in story.Location ?? new())
        {
            if (!IsValidEntity(loc.LocationName)) continue;
            var name = Normalize(loc.LocationName);
            var id = $"location:{name.ToLowerInvariant()}";
            if (nodes.ContainsKey(id)) continue;

            var desc = "";
            if (loc.LocationDescription != null && loc.LocationDescription.Count > 0)
                desc = loc.LocationDescription.FirstOrDefault()?.Description ?? "";

            nodes[id] = new GraphNode
            {
                Id = id,
                Label = name,
                Type = NodeType.Location,
                Properties = new() { ["description"] = desc }
            };
        }

        // Timelines
        foreach (var tl in story.Timeline ?? new())
        {
            if (!IsValidEntity(tl.TimelineName)) continue;
            var name = Normalize(tl.TimelineName);
            var id = $"timeline:{name.ToLowerInvariant()}";
            if (nodes.ContainsKey(id)) continue;

            nodes[id] = new GraphNode
            {
                Id = id,
                Label = name,
                Type = NodeType.Timeline,
                Properties = new()
                {
                    ["description"] = tl.TimelineDescription ?? "",
                    ["startDate"] = tl.StartDate?.ToString("o") ?? "",
                    ["endDate"] = tl.StopDate?.ToString("o") ?? ""
                }
            };
        }

        // Chapters & Paragraphs
        foreach (var ch in story.Chapter ?? new())
        {
            if (!IsValidEntity(ch.ChapterName)) continue;
            var chTitle = Normalize(ch.ChapterName);
            var chId = $"chapter:{chTitle.ToLowerInvariant()}";
            if (!nodes.ContainsKey(chId))
            {
                nodes[chId] = new GraphNode
                {
                    Id = chId,
                    Label = chTitle,
                    Type = NodeType.Chapter,
                    Properties = new()
                    {
                        ["index"] = ch.Sequence.ToString(),
                        ["beatsSummary"] = ch.Synopsis ?? ""
                    }
                };
            }

            // Track chapter-level sets for edges
            var chapterCharacters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chapterLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chapterTimelines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in ch.Paragraph ?? new())
            {
                var pId = $"paragraph:{chTitle.ToLowerInvariant()}:p{p.Sequence}";
                var pLocName = p.Location?.LocationName ?? "";
                var pTlName = p.Timeline?.TimelineName ?? "";

                if (!nodes.ContainsKey(pId))
                {
                    nodes[pId] = new GraphNode
                    {
                        Id = pId,
                        Label = $"P{p.Sequence}",
                        Type = NodeType.Paragraph,
                        Properties = new()
                        {
                            ["text"] = p.ParagraphContent ?? "",
                            ["location"] = pLocName,
                            ["timeline"] = pTlName
                        }
                    };
                }

                // Edge: CONTAINS (Chapter → Paragraph)
                AddEdge(graph, edgeIds, chId, pId, "CONTAINS");

                // Characters in paragraph
                var paraCharNames = (p.Characters ?? new())
                    .Select(c => c.CharacterName)
                    .Where(n => IsValidEntity(n))
                    .Select(n => ResolveCharacterName(Normalize(n), knownCharNames))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var charName in paraCharNames)
                {
                    var charId = $"character:{charName.ToLowerInvariant()}";
                    // Ensure node exists
                    if (!nodes.ContainsKey(charId))
                    {
                        nodes[charId] = new GraphNode
                        {
                            Id = charId,
                            Label = charName,
                            Type = NodeType.Character,
                            Properties = new() { ["role"] = "", ["backstory"] = "" }
                        };
                    }

                    // MENTIONED_IN
                    AddEdge(graph, edgeIds, charId, pId, "MENTIONED_IN");
                    // APPEARS_IN
                    AddEdge(graph, edgeIds, charId, chId, "APPEARS_IN");
                    chapterCharacters.Add(charName);

                    // SEEN_AT
                    if (IsValidEntity(pLocName))
                    {
                        var locId = $"location:{Normalize(pLocName).ToLowerInvariant()}";
                        AddEdge(graph, edgeIds, charId, locId, "SEEN_AT");
                    }
                }

                // INTERACTS_WITH (pairs of characters in same paragraph)
                for (int i = 0; i < paraCharNames.Count; i++)
                {
                    for (int j = i + 1; j < paraCharNames.Count; j++)
                    {
                        var a = paraCharNames[i];
                        var b = paraCharNames[j];
                        // Sort for determinism
                        var pair = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0
                            ? (a, b) : (b, a);
                        var aId = $"character:{pair.Item1.ToLowerInvariant()}";
                        var bId = $"character:{pair.Item2.ToLowerInvariant()}";
                        AddEdge(graph, edgeIds, aId, bId, "INTERACTS_WITH");
                    }
                }

                if (IsValidEntity(pLocName)) chapterLocations.Add(Normalize(pLocName));
                if (IsValidEntity(pTlName)) chapterTimelines.Add(Normalize(pTlName));
            }

            // SETTING_OF
            foreach (var locName in chapterLocations)
            {
                var locId = $"location:{locName.ToLowerInvariant()}";
                if (!nodes.ContainsKey(locId))
                {
                    nodes[locId] = new GraphNode
                    {
                        Id = locId, Label = locName, Type = NodeType.Location,
                        Properties = new() { ["description"] = "" }
                    };
                }
                AddEdge(graph, edgeIds, locId, chId, "SETTING_OF");
            }

            // COVERS
            foreach (var tlName in chapterTimelines)
            {
                var tlId = $"timeline:{tlName.ToLowerInvariant()}";
                if (!nodes.ContainsKey(tlId))
                {
                    nodes[tlId] = new GraphNode
                    {
                        Id = tlId, Label = tlName, Type = NodeType.Timeline,
                        Properties = new() { ["description"] = "", ["startDate"] = "", ["endDate"] = "" }
                    };
                }
                AddEdge(graph, edgeIds, tlId, chId, "COVERS");
            }
        }

        // ACTIVE_ON (CharacterBackground → Timeline)
        foreach (var c in story.Character ?? new())
        {
            if (!IsValidEntity(c.CharacterName)) continue;
            var charId = $"character:{Normalize(c.CharacterName).ToLowerInvariant()}";
            foreach (var bg in c.CharacterBackground ?? new())
            {
                var tlName = bg.Timeline?.TimelineName;
                if (!IsValidEntity(tlName)) continue;
                var tlId = $"timeline:{Normalize(tlName).ToLowerInvariant()}";
                AddEdge(graph, edgeIds, charId, tlId, "ACTIVE_ON");
            }
        }

        graph.Nodes = nodes.Values.ToList();
        return graph;
    }

    private static void AddEdge(StoryGraph graph, HashSet<string> edgeIds,
        string sourceId, string targetId, string label)
    {
        var edgeId = $"{sourceId}--{label}--{targetId}";
        if (edgeIds.Add(edgeId))
        {
            graph.Edges.Add(new GraphEdge
            {
                Id = edgeId,
                SourceId = sourceId,
                TargetId = targetId,
                Label = label
            });
        }
    }

    private static string ResolveCharacterName(string name, List<string> knownNames)
    {
        // 1. Exact case-insensitive match
        var exact = knownNames.FirstOrDefault(k =>
            k.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // 2. Contains match
        var contains = knownNames.FirstOrDefault(k =>
            name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            k.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (contains is not null) return contains;

        // 3. Levenshtein distance <= 2
        var closest = knownNames
            .Select(k => (Name: k, Distance: LevenshteinDistance(
                name.ToLowerInvariant(), k.ToLowerInvariant())))
            .Where(x => x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .FirstOrDefault();
        if (closest.Name is not null) return closest.Name;

        return name;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(
                    d[i - 1, j] + 1,
                    d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private static bool IsValidEntity(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string Normalize(string value)
    {
        return System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"[\s_-]+", " ");
    }
}
