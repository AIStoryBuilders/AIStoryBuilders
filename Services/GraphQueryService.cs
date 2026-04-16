using AIStoryBuilders.Models;

namespace AIStoryBuilders.Services;

public interface IGraphQueryService
{
    List<CharacterDto> GetCharacters();
    List<LocationDto> GetLocations();
    List<TimelineDto> GetTimelines();
    List<ChapterDto> GetChapters();
    ParagraphDto GetParagraph(string chapterTitle, int paragraphIndex);
    List<RelationshipDto> GetCharacterRelationships(string characterName);
    List<AppearanceDto> GetCharacterAppearances(string characterName);
    List<CharacterDto> GetChapterCharacters(string chapterTitle);
    List<LocationUsageDto> GetLocationUsage(string locationName);
    List<InteractionDto> GetCharacterInteractions(string characterName);
    List<string> GetTimelineChapters(string timelineName);
    List<OrphanDto> GetOrphanedNodes();
    List<ArcStepDto> GetCharacterArc(string characterName);
    List<LocationEventDto> GetLocationTimeline(string locationName);
    GraphSummaryDto GetGraphSummary();
    StoryDetailsDto GetStoryDetails();
    string GetStoryStyle();
    string GetStoryTheme();
    string GetStorySynopsis();
    string GetStoryWorldFacts();
}

public class GraphQueryService : IGraphQueryService
{
    public List<CharacterDto> GetCharacters()
    {
        var story = GraphState.CurrentStory;
        if (story == null) return new();

        return (story.Character ?? new())
            .Select(c => new CharacterDto
            {
                Name = c.CharacterName ?? "",
                Role = c.CharacterBackground?.FirstOrDefault()?.Type ?? "",
                Backstory = string.Join("; ",
                    (c.CharacterBackground ?? new()).Select(bg => bg.Description ?? "").Where(d => d.Length > 0))
            }).ToList();
    }

    public List<LocationDto> GetLocations()
    {
        var story = GraphState.CurrentStory;
        if (story == null) return new();

        return (story.Location ?? new())
            .Select(l => new LocationDto
            {
                Name = l.LocationName ?? "",
                Description = l.LocationDescription?.FirstOrDefault()?.Description ?? ""
            }).ToList();
    }

    public List<TimelineDto> GetTimelines()
    {
        var story = GraphState.CurrentStory;
        if (story == null) return new();

        return (story.Timeline ?? new())
            .Select(t => new TimelineDto
            {
                Name = t.TimelineName ?? "",
                Description = t.TimelineDescription ?? "",
                Start = t.StartDate,
                End = t.StopDate
            }).ToList();
    }

    public List<ChapterDto> GetChapters()
    {
        var story = GraphState.CurrentStory;
        if (story == null) return new();

        return (story.Chapter ?? new())
            .OrderBy(c => c.Sequence)
            .Select(c => new ChapterDto
            {
                Title = c.ChapterName ?? "",
                BeatsSummary = c.Synopsis ?? "",
                ParagraphCount = c.Paragraph?.Count ?? 0
            }).ToList();
    }

    public ParagraphDto GetParagraph(string chapterTitle, int paragraphIndex)
    {
        var story = GraphState.CurrentStory;
        if (story == null) return null;

        var chapter = (story.Chapter ?? new())
            .FirstOrDefault(c => (c.ChapterName ?? "").Equals(chapterTitle, StringComparison.OrdinalIgnoreCase));
        if (chapter == null) return null;

        var para = (chapter.Paragraph ?? new())
            .FirstOrDefault(p => p.Sequence == paragraphIndex);
        if (para == null) return null;

        return new ParagraphDto
        {
            Text = para.ParagraphContent ?? "",
            Location = para.Location?.LocationName ?? "",
            Timeline = para.Timeline?.TimelineName ?? "",
            Characters = (para.Characters ?? new()).Select(c => c.CharacterName ?? "").ToList()
        };
    }

    public List<RelationshipDto> GetCharacterRelationships(string characterName)
    {
        var graph = GraphState.Current;
        if (graph == null) return new();

        var charId = $"character:{characterName.ToLowerInvariant().Trim()}";
        return graph.Edges
            .Where(e => e.SourceId.Equals(charId, StringComparison.OrdinalIgnoreCase) ||
                        e.TargetId.Equals(charId, StringComparison.OrdinalIgnoreCase))
            .Select(e => new RelationshipDto
            {
                RelatedTo = e.SourceId.Equals(charId, StringComparison.OrdinalIgnoreCase)
                    ? e.TargetId : e.SourceId,
                EdgeLabel = e.Label
            }).ToList();
    }

    public List<AppearanceDto> GetCharacterAppearances(string characterName)
    {
        var graph = GraphState.Current;
        if (graph == null) return new();

        var charId = $"character:{characterName.ToLowerInvariant().Trim()}";
        return graph.Edges
            .Where(e => e.SourceId.Equals(charId, StringComparison.OrdinalIgnoreCase) &&
                        e.Label == "MENTIONED_IN")
            .Select(e =>
            {
                // paragraph id format: paragraph:{chapter}:p{index}
                var parts = e.TargetId.Split(':');
                var chapter = parts.Length >= 2 ? parts[1] : "";
                var pIndex = 0;
                if (parts.Length >= 3 && parts[2].StartsWith("p"))
                    int.TryParse(parts[2][1..], out pIndex);
                return new AppearanceDto { Chapter = chapter, ParagraphIndex = pIndex };
            }).ToList();
    }

    public List<CharacterDto> GetChapterCharacters(string chapterTitle)
    {
        var graph = GraphState.Current;
        var story = GraphState.CurrentStory;
        if (graph == null || story == null) return new();

        var chId = $"chapter:{chapterTitle.ToLowerInvariant().Trim()}";
        var charIds = graph.Edges
            .Where(e => e.Label == "APPEARS_IN" &&
                        e.TargetId.Equals(chId, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return graph.Nodes
            .Where(n => n.Type == NodeType.Character && charIds.Contains(n.Id))
            .Select(n => new CharacterDto
            {
                Name = n.Label,
                Role = n.Properties.GetValueOrDefault("role", ""),
                Backstory = n.Properties.GetValueOrDefault("backstory", "")
            }).ToList();
    }

    public List<LocationUsageDto> GetLocationUsage(string locationName)
    {
        var graph = GraphState.Current;
        if (graph == null) return new();

        var locId = $"location:{locationName.ToLowerInvariant().Trim()}";

        // Find chapters where this location is a SETTING_OF
        var chapterIds = graph.Edges
            .Where(e => e.Label == "SETTING_OF" &&
                        e.SourceId.Equals(locId, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.TargetId)
            .ToList();

        return chapterIds.Select(chId =>
        {
            var chNode = graph.Nodes.FirstOrDefault(n => n.Id.Equals(chId, StringComparison.OrdinalIgnoreCase));
            // Characters SEEN_AT this location
            var chars = graph.Edges
                .Where(e => e.Label == "SEEN_AT" &&
                            e.TargetId.Equals(locId, StringComparison.OrdinalIgnoreCase))
                .Select(e => graph.Nodes.FirstOrDefault(n =>
                    n.Id.Equals(e.SourceId, StringComparison.OrdinalIgnoreCase))?.Label ?? "")
                .Where(n => n.Length > 0)
                .Distinct()
                .ToList();

            return new LocationUsageDto
            {
                Chapter = chNode?.Label ?? chId,
                Characters = chars
            };
        }).ToList();
    }

    public List<InteractionDto> GetCharacterInteractions(string characterName)
    {
        var graph = GraphState.Current;
        if (graph == null) return new();

        var charId = $"character:{characterName.ToLowerInvariant().Trim()}";

        var interactEdges = graph.Edges
            .Where(e => e.Label == "INTERACTS_WITH" &&
                        (e.SourceId.Equals(charId, StringComparison.OrdinalIgnoreCase) ||
                         e.TargetId.Equals(charId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var otherIds = interactEdges
            .Select(e => e.SourceId.Equals(charId, StringComparison.OrdinalIgnoreCase)
                ? e.TargetId : e.SourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return otherIds.Select(otherId =>
        {
            var otherNode = graph.Nodes.FirstOrDefault(n =>
                n.Id.Equals(otherId, StringComparison.OrdinalIgnoreCase));

            // Find chapters where both appear
            var myChapters = graph.Edges
                .Where(e => e.Label == "APPEARS_IN" &&
                            e.SourceId.Equals(charId, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.TargetId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var theirChapters = graph.Edges
                .Where(e => e.Label == "APPEARS_IN" &&
                            e.SourceId.Equals(otherId, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.TargetId);
            var shared = theirChapters.Where(c => myChapters.Contains(c))
                .Select(cId => graph.Nodes.FirstOrDefault(n =>
                    n.Id.Equals(cId, StringComparison.OrdinalIgnoreCase))?.Label ?? cId)
                .ToList();

            return new InteractionDto
            {
                InteractsWith = otherNode?.Label ?? otherId,
                Chapters = shared
            };
        }).ToList();
    }

    public List<string> GetTimelineChapters(string timelineName)
    {
        var graph = GraphState.Current;
        if (graph == null) return new();

        var tlId = $"timeline:{timelineName.ToLowerInvariant().Trim()}";
        return graph.Edges
            .Where(e => e.Label == "COVERS" &&
                        e.SourceId.Equals(tlId, StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                var node = graph.Nodes.FirstOrDefault(n =>
                    n.Id.Equals(e.TargetId, StringComparison.OrdinalIgnoreCase));
                return node?.Label ?? e.TargetId;
            }).ToList();
    }

    public List<OrphanDto> GetOrphanedNodes()
    {
        var graph = GraphState.Current;
        if (graph == null) return new();

        var connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in graph.Edges)
        {
            connectedIds.Add(e.SourceId);
            connectedIds.Add(e.TargetId);
        }

        return graph.Nodes
            .Where(n => !connectedIds.Contains(n.Id))
            .Select(n => new OrphanDto
            {
                Id = n.Id,
                Type = n.Type.ToString(),
                Label = n.Label
            }).ToList();
    }

    public List<ArcStepDto> GetCharacterArc(string characterName)
    {
        var story = GraphState.CurrentStory;
        if (story == null) return new();

        var results = new List<ArcStepDto>();
        var name = characterName.Trim();

        foreach (var ch in (story.Chapter ?? new()).OrderBy(c => c.Sequence))
        {
            foreach (var p in (ch.Paragraph ?? new()).OrderBy(p => p.Sequence))
            {
                var charNames = (p.Characters ?? new()).Select(c => c.CharacterName ?? "").ToList();
                if (!charNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                results.Add(new ArcStepDto
                {
                    Chapter = ch.ChapterName ?? "",
                    Location = p.Location?.LocationName ?? "",
                    Timeline = p.Timeline?.TimelineName ?? "",
                    CoCharacters = charNames
                        .Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase) && n.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }
        }

        return results;
    }

    public List<LocationEventDto> GetLocationTimeline(string locationName)
    {
        var story = GraphState.CurrentStory;
        if (story == null) return new();

        var results = new List<LocationEventDto>();
        var name = locationName.Trim();

        foreach (var ch in (story.Chapter ?? new()).OrderBy(c => c.Sequence))
        {
            foreach (var p in (ch.Paragraph ?? new()).OrderBy(p => p.Sequence))
            {
                var loc = p.Location?.LocationName ?? "";
                if (!loc.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

                results.Add(new LocationEventDto
                {
                    Chapter = ch.ChapterName ?? "",
                    Timeline = p.Timeline?.TimelineName ?? "",
                    Characters = (p.Characters ?? new())
                        .Select(c => c.CharacterName ?? "")
                        .Where(n => n.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }
        }

        return results;
    }

    public GraphSummaryDto GetGraphSummary()
    {
        var graph = GraphState.Current;
        if (graph == null) return new GraphSummaryDto();

        return new GraphSummaryDto
        {
            NodeCount = graph.Nodes.Count,
            EdgeCount = graph.Edges.Count,
            ByType = graph.Nodes
                .GroupBy(n => n.Type.ToString())
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    public StoryDetailsDto GetStoryDetails()
    {
        var story = GraphState.CurrentStory;
        if (story == null) return new StoryDetailsDto();

        return new StoryDetailsDto
        {
            Title = story.Title ?? "",
            Style = story.Style ?? "",
            Theme = story.Theme ?? "",
            Synopsis = story.Synopsis ?? "",
            WorldFacts = story.WorldFacts ?? ""
        };
    }

    public string GetStoryStyle()
    {
        return GraphState.CurrentStory?.Style ?? "";
    }

    public string GetStoryTheme()
    {
        return GraphState.CurrentStory?.Theme ?? "";
    }

    public string GetStorySynopsis()
    {
        return GraphState.CurrentStory?.Synopsis ?? "";
    }

    public string GetStoryWorldFacts()
    {
        return GraphState.CurrentStory?.WorldFacts ?? "";
    }
}
