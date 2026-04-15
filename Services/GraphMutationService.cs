using AIStoryBuilders.Models;
using Location = AIStoryBuilders.Models.Location;

namespace AIStoryBuilders.Services;

public interface IGraphMutationService
{
    Task<MutationResult> AddCharacterAsync(string name, string type, string description, string timelineName, bool confirmed);
    Task<MutationResult> UpdateCharacterAsync(string name, string type, string description, string timelineName, bool confirmed);
    Task<MutationResult> RenameCharacterAsync(string currentName, string newName, bool confirmed);
    Task<MutationResult> DeleteCharacterAsync(string name, bool confirmed);

    Task<MutationResult> AddLocationAsync(string name, string description, string timelineName, bool confirmed);
    Task<MutationResult> UpdateLocationAsync(string name, string description, string timelineName, bool confirmed);
    Task<MutationResult> RenameLocationAsync(string currentName, string newName, bool confirmed);
    Task<MutationResult> DeleteLocationAsync(string name, bool confirmed);

    Task<MutationResult> AddTimelineAsync(string name, string description, DateTime startDate, DateTime? stopDate, bool confirmed);
    Task<MutationResult> UpdateTimelineAsync(string name, string description, DateTime startDate, DateTime? stopDate, bool confirmed);
    Task<MutationResult> RenameTimelineAsync(string currentName, string newName, string description, DateTime startDate, DateTime? stopDate, bool confirmed);
    Task<MutationResult> DeleteTimelineAsync(string name, bool confirmed);

    Task<MutationResult> AddChapterAsync(string synopsis, int? insertAtPosition, bool confirmed);
    Task<MutationResult> UpdateChapterAsync(int chapterNumber, string synopsis, bool confirmed);
    Task<MutationResult> DeleteChapterAsync(int chapterNumber, bool confirmed);

    Task<MutationResult> AddParagraphAsync(int chapterNumber, int position, string locationName, string timelineName, List<string> characterNames, bool confirmed);
    Task<MutationResult> UpdateParagraphAsync(int chapterNumber, int paragraphNumber, string content, string locationName, string timelineName, List<string> characterNames, bool confirmed);
    Task<MutationResult> DeleteParagraphAsync(int chapterNumber, int paragraphNumber, bool confirmed);

    Task<MutationResult> UpdateStoryDetailsAsync(string style, string theme, string synopsis, bool confirmed);
    Task<MutationResult> ReEmbedStoryAsync(bool confirmed);
}

public class GraphMutationService : IGraphMutationService
{
    private readonly AIStoryBuildersService _storyService;
    private readonly IGraphBuilder _graphBuilder;

    public GraphMutationService(AIStoryBuildersService storyService, IGraphBuilder graphBuilder)
    {
        _storyService = storyService;
        _graphBuilder = graphBuilder;
    }

    // ── Character ──────────────────────────────────────────────

    public async Task<MutationResult> AddCharacterAsync(
        string name, string type, string description, string timelineName, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "AddCharacter",
            Entity = name,
            Summary = $"Add character '{name}' ({type}) with description: {Truncate(description, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var character = new Character
        {
            CharacterName = name,
            Story = GraphState.CurrentStory,
            CharacterBackground = new List<CharacterBackground>
            {
                new CharacterBackground
                {
                    Type = type,
                    Timeline = string.IsNullOrEmpty(timelineName)
                        ? null : new Timeline { TimelineName = timelineName },
                    Description = SanitizePipe(description)
                }
            }
        };

        await _storyService.AddUpdateCharacterAsync(character, name);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Characters/{name}.csv");
        return result;
    }

    public async Task<MutationResult> UpdateCharacterAsync(
        string name, string type, string description, string timelineName, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "UpdateCharacter",
            Entity = name,
            Summary = $"Update character '{name}' — type: {type}, description: {Truncate(description, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var character = new Character
        {
            CharacterName = name,
            Story = GraphState.CurrentStory,
            CharacterBackground = new List<CharacterBackground>
            {
                new CharacterBackground
                {
                    Type = type,
                    Timeline = string.IsNullOrEmpty(timelineName)
                        ? null : new Timeline { TimelineName = timelineName },
                    Description = SanitizePipe(description)
                }
            }
        };

        await _storyService.AddUpdateCharacterAsync(character, name);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Characters/{name}.csv");
        return result;
    }

    public async Task<MutationResult> RenameCharacterAsync(string currentName, string newName, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "RenameCharacter",
            Entity = currentName,
            Summary = $"Rename character '{currentName}' → '{newName}' (updates all paragraph references)"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var character = new Character
        {
            CharacterName = newName,
            Story = GraphState.CurrentStory
        };
        _storyService.UpdateCharacterName(character, currentName);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Characters/{newName}.csv (renamed from {currentName}.csv)");
        result.AffectedFiles.Add("All paragraph files with character references updated");
        return result;
    }

    public async Task<MutationResult> DeleteCharacterAsync(string name, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "DeleteCharacter",
            Entity = name,
            Summary = $"Delete character '{name}' and remove from all paragraph character lists"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var character = new Character
        {
            CharacterName = name,
            Story = GraphState.CurrentStory
        };
        _storyService.DeleteCharacter(character, name);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Characters/{name}.csv (deleted)");
        result.AffectedFiles.Add("All paragraph files with character references updated");
        return result;
    }

    // ── Location ───────────────────────────────────────────────

    public async Task<MutationResult> AddLocationAsync(
        string name, string description, string timelineName, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "AddLocation",
            Entity = name,
            Summary = $"Add location '{name}': {Truncate(description, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var location = new Location
        {
            LocationName = name,
            Story = GraphState.CurrentStory,
            LocationDescription = new List<LocationDescription>
            {
                new LocationDescription
                {
                    Description = SanitizePipe(description),
                    Timeline = string.IsNullOrEmpty(timelineName)
                        ? null : new Timeline { TimelineName = timelineName }
                }
            }
        };

        await _storyService.AddLocationAsync(location);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Locations/{name}.csv");
        return result;
    }

    public async Task<MutationResult> UpdateLocationAsync(
        string name, string description, string timelineName, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "UpdateLocation",
            Entity = name,
            Summary = $"Update location '{name}': {Truncate(description, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var location = new Location
        {
            LocationName = name,
            Story = GraphState.CurrentStory,
            LocationDescription = new List<LocationDescription>
            {
                new LocationDescription
                {
                    Description = SanitizePipe(description),
                    Timeline = string.IsNullOrEmpty(timelineName)
                        ? null : new Timeline { TimelineName = timelineName }
                }
            }
        };

        await _storyService.UpdateLocationDescriptions(location);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Locations/{name}.csv");
        return result;
    }

    public async Task<MutationResult> RenameLocationAsync(string currentName, string newName, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "RenameLocation",
            Entity = currentName,
            Summary = $"Rename location '{currentName}' → '{newName}' (updates all paragraph references)"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var location = new Location
        {
            LocationName = newName,
            Story = GraphState.CurrentStory
        };
        _storyService.UpdateLocationName(location, currentName);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Locations/{newName}.csv (renamed from {currentName}.csv)");
        result.AffectedFiles.Add("All paragraph files with location references updated");
        return result;
    }

    public async Task<MutationResult> DeleteLocationAsync(string name, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "DeleteLocation",
            Entity = name,
            Summary = $"Delete location '{name}' and clear from all paragraph references"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var location = new Location
        {
            LocationName = name,
            Story = GraphState.CurrentStory
        };
        _storyService.DeleteLocation(location);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Locations/{name}.csv (deleted)");
        result.AffectedFiles.Add("All paragraph files with location references cleared");
        return result;
    }

    // ── Timeline ───────────────────────────────────────────────

    public async Task<MutationResult> AddTimelineAsync(
        string name, string description, DateTime startDate, DateTime? stopDate, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "AddTimeline",
            Entity = name,
            Summary = $"Add timeline '{name}': {Truncate(description, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var timeline = new Timeline
        {
            TimelineName = name,
            TimelineDescription = SanitizePipe(description),
            StartDate = startDate,
            StopDate = stopDate,
            Story = GraphState.CurrentStory
        };
        _storyService.AddTimeline(timeline);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add("Timelines.csv");
        return result;
    }

    public async Task<MutationResult> UpdateTimelineAsync(
        string name, string description, DateTime startDate, DateTime? stopDate, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "UpdateTimeline",
            Entity = name,
            Summary = $"Update timeline '{name}': {Truncate(description, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var timeline = new Timeline
        {
            TimelineName = name,
            TimelineDescription = SanitizePipe(description),
            StartDate = startDate,
            StopDate = stopDate,
            Story = GraphState.CurrentStory
        };
        _storyService.UpdateTimeline(timeline, name);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add("Timelines.csv");
        return result;
    }

    public async Task<MutationResult> RenameTimelineAsync(
        string currentName, string newName, string description,
        DateTime startDate, DateTime? stopDate, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "RenameTimeline",
            Entity = currentName,
            Summary = $"Rename timeline '{currentName}' → '{newName}' (updates all characters, locations, and paragraphs)"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var timeline = new Timeline
        {
            TimelineName = newName,
            TimelineDescription = SanitizePipe(description),
            StartDate = startDate,
            StopDate = stopDate,
            Story = GraphState.CurrentStory
        };
        await _storyService.UpdateTimelineAndTimelineNameAsync(timeline, currentName);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add("Timelines.csv");
        result.AffectedFiles.Add("All character files (re-embedded)");
        result.AffectedFiles.Add("All location files (re-embedded)");
        result.AffectedFiles.Add("All paragraph files with timeline references updated");
        return result;
    }

    public async Task<MutationResult> DeleteTimelineAsync(string name, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "DeleteTimeline",
            Entity = name,
            Summary = $"Delete timeline '{name}' and clear from all characters, locations, and paragraphs"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var timeline = new Timeline
        {
            TimelineName = name,
            Story = GraphState.CurrentStory
        };
        await _storyService.DeleteTimelineAndTimelineNameAsync(timeline, name);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add("Timelines.csv");
        result.AffectedFiles.Add("All character files (re-embedded)");
        result.AffectedFiles.Add("All location files (re-embedded)");
        result.AffectedFiles.Add("All paragraph files with timeline references cleared");
        return result;
    }

    // ── Chapter ────────────────────────────────────────────────

    public async Task<MutationResult> AddChapterAsync(string synopsis, int? insertAtPosition, bool confirmed)
    {
        var story = GraphState.CurrentStory;
        int totalChapters = _storyService.CountChapters(story);
        int position = insertAtPosition ?? (totalChapters + 1);

        var result = new MutationResult
        {
            Operation = insertAtPosition.HasValue ? "InsertChapter" : "AddChapter",
            Entity = $"Chapter {position}",
            Summary = $"Add chapter at position {position}: {Truncate(synopsis, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var chapter = new Chapter
        {
            ChapterName = $"Chapter {position}",
            Sequence = position,
            Synopsis = SanitizePipe(synopsis),
            Story = story
        };

        if (insertAtPosition.HasValue && insertAtPosition.Value <= totalChapters)
        {
            _storyService.RestructureChapters(chapter, RestructureType.Add);
            await _storyService.InsertChapterAsync(chapter);
        }
        else
        {
            string chapterFolderName = $"Chapter{position}";
            await _storyService.AddChapterAsync(chapter, chapterFolderName);
        }

        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Chapters/Chapter{position}/Chapter{position}.txt");
        return result;
    }

    public async Task<MutationResult> UpdateChapterAsync(int chapterNumber, string synopsis, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "UpdateChapter",
            Entity = $"Chapter {chapterNumber}",
            Summary = $"Update Chapter {chapterNumber} synopsis: {Truncate(synopsis, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var chapter = new Chapter
        {
            ChapterName = $"Chapter {chapterNumber}",
            Sequence = chapterNumber,
            Synopsis = SanitizePipe(synopsis),
            Story = GraphState.CurrentStory
        };
        await _storyService.UpdateChapterAsync(chapter);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Chapters/Chapter{chapterNumber}/Chapter{chapterNumber}.txt");
        return result;
    }

    public async Task<MutationResult> DeleteChapterAsync(int chapterNumber, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "DeleteChapter",
            Entity = $"Chapter {chapterNumber}",
            Summary = $"Delete Chapter {chapterNumber} and all its paragraphs, then renumber subsequent chapters"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var chapter = new Chapter
        {
            ChapterName = $"Chapter {chapterNumber}",
            Sequence = chapterNumber,
            Story = GraphState.CurrentStory
        };
        _storyService.DeleteChapter(chapter);
        _storyService.RestructureChapters(chapter, RestructureType.Delete);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Chapters/Chapter{chapterNumber}/ (deleted)");
        result.AffectedFiles.Add("Subsequent chapter folders renumbered");
        return result;
    }

    // ── Paragraph ──────────────────────────────────────────────

    public async Task<MutationResult> AddParagraphAsync(
        int chapterNumber, int position, string locationName,
        string timelineName, List<string> characterNames, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "AddParagraph",
            Entity = $"Chapter {chapterNumber}, Paragraph {position}",
            Summary = $"Insert empty paragraph at Chapter {chapterNumber}, position {position} (location: {locationName}, timeline: {timelineName})"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var chapter = new Chapter
        {
            ChapterName = $"Chapter {chapterNumber}",
            Sequence = chapterNumber,
            Story = GraphState.CurrentStory
        };
        var paragraph = new Paragraph
        {
            Sequence = position,
            Location = new Location { LocationName = locationName },
            Timeline = new Timeline { TimelineName = timelineName },
            Characters = (characterNames ?? new())
                .Select(n => new Character { CharacterName = n }).ToList()
        };

        _storyService.AddParagraph(chapter, paragraph);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Chapters/Chapter{chapterNumber}/Paragraph{position}.txt");
        result.AffectedFiles.Add("Subsequent paragraph files renumbered");
        return result;
    }

    public async Task<MutationResult> UpdateParagraphAsync(
        int chapterNumber, int paragraphNumber, string content,
        string locationName, string timelineName,
        List<string> characterNames, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "UpdateParagraph",
            Entity = $"Chapter {chapterNumber}, Paragraph {paragraphNumber}",
            Summary = $"Update paragraph {paragraphNumber} in Chapter {chapterNumber}: {Truncate(content, 80)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var chapter = new Chapter
        {
            ChapterName = $"Chapter {chapterNumber}",
            Sequence = chapterNumber,
            Story = GraphState.CurrentStory
        };
        var paragraph = new Paragraph
        {
            Sequence = paragraphNumber,
            ParagraphContent = SanitizePipe(content),
            Location = new Location { LocationName = locationName },
            Timeline = new Timeline { TimelineName = timelineName },
            Characters = (characterNames ?? new())
                .Select(n => new Character { CharacterName = n }).ToList()
        };

        await _storyService.UpdateParagraph(chapter, paragraph);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Chapters/Chapter{chapterNumber}/Paragraph{paragraphNumber}.txt");
        return result;
    }

    public async Task<MutationResult> DeleteParagraphAsync(
        int chapterNumber, int paragraphNumber, bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "DeleteParagraph",
            Entity = $"Chapter {chapterNumber}, Paragraph {paragraphNumber}",
            Summary = $"Delete paragraph {paragraphNumber} from Chapter {chapterNumber} and renumber remaining paragraphs"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        var chapter = new Chapter
        {
            ChapterName = $"Chapter {chapterNumber}",
            Sequence = chapterNumber,
            Story = GraphState.CurrentStory
        };
        var paragraph = new Paragraph { Sequence = paragraphNumber };

        _storyService.DeleteParagraph(chapter, paragraph);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add($"Chapters/Chapter{chapterNumber}/Paragraph{paragraphNumber}.txt (deleted)");
        result.AffectedFiles.Add("Subsequent paragraph files renumbered");
        return result;
    }

    // ── Story Details ──────────────────────────────────────────

    public async Task<MutationResult> UpdateStoryDetailsAsync(
        string style, string theme, string synopsis, bool confirmed)
    {
        var story = GraphState.CurrentStory;
        var result = new MutationResult
        {
            Operation = "UpdateStoryDetails",
            Entity = story?.Title ?? "",
            Summary = $"Update story details — style: {style ?? "(unchanged)"}, theme: {theme ?? "(unchanged)"}, synopsis: {Truncate(synopsis ?? "(unchanged)", 60)}"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        if (style != null) story.Style = SanitizePipe(style);
        if (theme != null) story.Theme = SanitizePipe(theme);
        if (synopsis != null) story.Synopsis = SanitizePipe(synopsis);

        _storyService.UpdateStory(story);
        await RefreshGraph();

        result.Success = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add("../AIStoryBuildersStories.csv");
        return result;
    }

    // ── Re-Embed ───────────────────────────────────────────────

    public async Task<MutationResult> ReEmbedStoryAsync(bool confirmed)
    {
        var result = new MutationResult
        {
            Operation = "ReEmbedStory",
            Entity = GraphState.CurrentStory?.Title ?? "",
            Summary = "Regenerate all embeddings for every paragraph, chapter, character, and location file in the story"
        };

        if (!confirmed) { result.IsPreview = true; result.Success = true; return result; }

        await _storyService.ReEmbedStory(GraphState.CurrentStory);
        await RefreshGraph();

        result.Success = true;
        result.EmbeddingsUpdated = true;
        result.GraphRefreshed = true;
        result.AffectedFiles.Add("All paragraph, chapter, character, and location files");
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────

    private Task RefreshGraph()
    {
        var story = GraphState.CurrentStory;
        if (story == null) return Task.CompletedTask;

        // Reload story data from disk
        var freshStory = ReloadStoryFromDisk(story.Title);
        var graph = _graphBuilder.Build(freshStory);
        GraphState.Current = graph;
        GraphState.CurrentStory = freshStory;
        return Task.CompletedTask;
    }

    private Story ReloadStoryFromDisk(string storyTitle)
    {
        // Reconstitute a full Story object from disk files
        var stories = _storyService.GetStorys();
        var story = stories.FirstOrDefault(s =>
            (s.Title ?? "").Equals(storyTitle, StringComparison.OrdinalIgnoreCase));
        if (story == null) return GraphState.CurrentStory;

        // Load all child entities
        story.Timeline = _storyService.GetTimelines(story);
        story.Location = _storyService.GetLocations(story);
        story.Character = _storyService.GetCharacters(story);
        story.Chapter = _storyService.GetChapters(story);

        foreach (var ch in story.Chapter)
        {
            ch.Paragraph = _storyService.GetParagraphs(ch);
            ch.Story = story;
        }

        return story;
    }

    private static string SanitizePipe(string input)
        => (input ?? "").Replace("|", "");

    private static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLen ? text : text[..maxLen] + "…";
    }
}
