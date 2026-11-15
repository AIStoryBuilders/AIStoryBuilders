#nullable disable
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Models.JSON;
using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;
using Character = AIStoryBuildersV2.Shared.Models.Character;

namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>
/// V1's story service (Services/AIStoryBuildersService*.cs), ported to Shared (plan §6.2). Files inside a story folder
/// are read and written with System.IO under <see cref="BasePath"/>: Documents\AIStoryBuilders on MAUI, MEMFS in the
/// browser (D5). The story index goes through <see cref="IStoryLibraryStore"/>, and every method that writes under
/// {Title}/ ends with <see cref="IStoryPersistence.MarkDirty"/> (the MarkDirty list in §6.2).
/// </summary>
public partial class AIStoryBuildersService
{
    public event EventHandler<TextEventArgs> TextEvent;

    private readonly AppMetadata _appMetadata;
    private readonly IHostCapabilities _host;
    private readonly IStoryLibraryStore _library;
    private readonly IStoryPersistence _persistence;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly StoryGraphCache _graphCache;
    private readonly IGraphBuilder _graphBuilder;
    private LogService LogService { get; }
    private OrchestratorMethods OrchestratorMethods { get; }

    public AIStoryBuildersService(
        IHostCapabilities host,
        IStoryLibraryStore library,
        IStoryPersistence persistence,
        LogService logService,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        OrchestratorMethods orchestratorMethods,
        StoryGraphCache graphCache,
        IGraphBuilder graphBuilder,
        AppMetadata appMetadata)
    {
        _host = host;
        _library = library;
        _persistence = persistence;
        LogService = logService;
        _embeddingGenerator = embeddingGenerator;
        OrchestratorMethods = orchestratorMethods;
        _graphCache = graphCache;
        _graphBuilder = graphBuilder;
        _appMetadata = appMetadata;
    }

    /// <summary>The stories root (V1's BasePath).</summary>
    public string BasePath => _host.RootPath;

    /// <summary>{BasePath}/{Title}.</summary>
    public string StoryPath(string title) => Path.Combine(BasePath, title);

    /// <summary>{BasePath}/{Title}/Chapters/Chapter{N} for a chapter named "Chapter N".</summary>
    public string ChapterPath(Chapter chapter) => Path.Combine(StoryPath(chapter.Story.Title), "Chapters", ChapterFolderName(chapter));

    /// <summary>"Chapter 3" → "Chapter3" (V1-File-Format §4.2).</summary>
    public static string ChapterFolderName(Chapter chapter) => (chapter.ChapterName ?? "").Replace(" ", "");

    // Utility

    public void CreateDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }

    /// <summary>After a write under {Title}/: save it (WASM) and mark the story graph stale (§6.2, D17).</summary>
    private void Touched(string title)
    {
        _persistence.MarkDirty(title);
        _graphCache.MarkDirty(title);
    }

    protected void RaiseText(string message, int displayLength) => TextEvent?.Invoke(this, new TextEventArgs(message, displayLength));

    // Knowledge Graph Persistence

    private static readonly System.Text.Json.JsonSerializerOptions GraphJsonOptions = new()
    {
        WriteIndented = true,
        // V1 runs on Windows, so its graph files use CRLF (V1-File-Format §3.11).
        NewLine = StoryFormat.NewLine,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
    };

    public async Task PersistGraphAsync(Story story, Models.StoryGraph graph, string storyPath)
    {
        string graphDir = Path.Combine(storyPath, "Graph");
        CreateDirectory(graphDir);

        var manifest = new
        {
            storyTitle = story.Title ?? "",
            createdDate = DateTime.UtcNow.ToString("o"),
            version = _appMetadata.Version,
            nodeCount = graph.Nodes.Count,
            edgeCount = graph.Edges.Count
        };
        await StoryFormat.WriteAllTextAsync(Path.Combine(graphDir, "manifest.json"), System.Text.Json.JsonSerializer.Serialize(manifest, GraphJsonOptions));

        await StoryFormat.WriteAllTextAsync(Path.Combine(graphDir, "graph.json"), System.Text.Json.JsonSerializer.Serialize(graph, GraphJsonOptions));

        var metadata = new
        {
            title = story.Title ?? "",
            genre = story.Style ?? "",
            theme = story.Theme ?? "",
            synopsis = story.Synopsis ?? "",
            chapterCount = story.Chapter?.Count ?? 0,
            characterCount = story.Character?.Count ?? 0,
            locationCount = story.Location?.Count ?? 0,
            timelineCount = story.Timeline?.Count ?? 0
        };
        await StoryFormat.WriteAllTextAsync(Path.Combine(graphDir, "metadata.json"), System.Text.Json.JsonSerializer.Serialize(metadata, GraphJsonOptions));

        LogService.WriteToLog($"Graph persisted for '{story.Title}' — {graph.Nodes.Count} nodes, {graph.Edges.Count} edges");
    }

    public Models.StoryGraph LoadGraphFromDisk(string storyPath)
    {
        string graphJsonPath = Path.Combine(storyPath, "Graph", "graph.json");
        if (!File.Exists(graphJsonPath)) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Models.StoryGraph>(File.ReadAllText(graphJsonPath), GraphJsonOptions);
        }
        catch (Exception ex)
        {
            LogService.WriteToLog($"LoadGraphFromDisk failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Loads a story with its characters, locations, timelines, chapters and paragraphs (V1's LoadFullStory).</summary>
    public async Task<Story> LoadFullStoryAsync(Story storyMeta)
    {
        var csvStory = (await GetStorysAsync()).FirstOrDefault(s => StoryTitle.SameTitle(s.Title, storyMeta.Title));
        if (csvStory != null)
        {
            storyMeta.Id = csvStory.Id;
            storyMeta.Style = csvStory.Style;
            storyMeta.Theme = csvStory.Theme;
            storyMeta.Synopsis = csvStory.Synopsis;
            storyMeta.WorldFacts = csvStory.WorldFacts;
        }

        storyMeta.Character = GetCharacters(storyMeta);
        storyMeta.Location = GetLocations(storyMeta);
        storyMeta.Timeline = GetTimelines(storyMeta);
        storyMeta.Chapter = GetChapters(storyMeta);

        foreach (var ch in storyMeta.Chapter)
        {
            ch.Paragraph = GetParagraphs(ch);
        }

        return storyMeta;
    }

    #region public string GetOnlyJSON(string json)
    public string GetOnlyJSON(string json)
    {
        // Pattern captures the JSON object between ```json and ```
        const string pattern = @"```json\s*(\{[\s\S]*?\})\s*```";
        var match = Regex.Match(json, pattern, RegexOptions.Singleline);
        return match.Success
            ? match.Groups[1].Value   // the raw JSON
            : json;                  // fallback to original if no match
    }
    #endregion

    #region public string RemoveLineBreaks(string content)
    public string RemoveLineBreaks(string content)
    {
        string output = "";

        // Remove any line breaks
        output = content.Replace(Environment.NewLine, " ");
        output = output.Replace("\n", " ");
        output = output.Replace("\r", " ");

        return output;
    }
    #endregion

    #region public void CreateFile(string path, string content)
    public void CreateFile(string path, string content)
    {
        // Create file if it doesn't exist
        if (!File.Exists(path))
        {
            File.WriteAllText(path, content);
        }
    }
    #endregion

    #region public class TextEventArgs : EventArgs
    public class TextEventArgs : EventArgs
    {
        public string Message { get; set; }
        public int DisplayLength { get; set; }

        public TextEventArgs(string message, int displayLength)
        {
            Message = message;
            DisplayLength = displayLength;
        }
    }
    #endregion

    #region public List<Models.Character> SimplifyCharacter(List<Models.Character> colCharacters, Paragraph objParagraph)
    public List<Models.Character> SimplifyCharacter(List<Models.Character> colCharacters, Paragraph objParagraph)
    {
        // If the Paragraph has a Timeline selected, filter the CharacterBackground 
        // to only those that are in the Timeline or empty Timeline
        List<Models.Character> colCharactersInTimeline = new List<Models.Character>();

        if (objParagraph.Timeline.TimelineName != null && objParagraph.Timeline.TimelineName.Length > 0)
        {
            foreach (var character in colCharacters)
            {
                Models.Character objCharacter = new Models.Character();

                objCharacter.CharacterName = character.CharacterName;

                objCharacter.CharacterBackground = new List<CharacterBackground>();

                foreach (var background in character.CharacterBackground)
                {
                    if ((background.Timeline.TimelineName == objParagraph.Timeline.TimelineName) ||
                    (background.Timeline.TimelineName == null || background.Timeline.TimelineName == ""))
                    {
                        objCharacter.CharacterBackground.Add(background);
                    }
                }

                colCharactersInTimeline.Add(objCharacter);
            }
        }
        else
        {
            colCharactersInTimeline = colCharacters;
        }

        return colCharactersInTimeline;
    }
    #endregion

    // JSON Conversion Methods

    #region public List<Models.JSON.Character> ConvertToJSONCharacter(List<Models.Character> colCharacters, Paragraph objParagraph)
    public List<Models.JSON.Character> ConvertToJSONCharacter(List<Models.Character> colCharacters, Paragraph objParagraph)
    {
        List<Models.JSON.Character> colCharactersInTimeline = new List<Models.JSON.Character>();

        foreach (var character in colCharacters)
        {
            Models.JSON.Character objCharacter = new Models.JSON.Character
            {
                name = character.CharacterName,
                descriptions = character.CharacterBackground != null ? new Descriptions[character.CharacterBackground.Count] : new Descriptions[0]
            };

            int i = 0;
            foreach (var background in character.CharacterBackground ?? Enumerable.Empty<CharacterBackground>())
            {
                bool shouldAddDescription = ((objParagraph.Timeline.TimelineName == null || objParagraph.Timeline.TimelineName.Length == 0)
                                            || background.Timeline.TimelineName == objParagraph.Timeline.TimelineName);

                if (shouldAddDescription)
                {
                    string strTimelineName = "";

                    if (background.Timeline != null)
                    {
                        strTimelineName = objParagraph.Timeline.TimelineName;
                    }

                    Descriptions objDescriptions = new Descriptions
                    {
                        description = background.Description.Replace("\n", " "),
                        description_type = background.Type.Replace("\n", " "),
                        timeline_name = strTimelineName
                    };

                    objCharacter.descriptions[i++] = objDescriptions;
                }
            }

            colCharactersInTimeline.Add(objCharacter);
        }

        return colCharactersInTimeline;
    }
    #endregion

    #region public Models.JSON.Paragraphs ConvertToJSONParagraph(Paragraph objParagraph)
    public Models.JSON.JSONParagraphs ConvertToJSONParagraph(Paragraph objParagraph)
    {
        Models.JSON.JSONParagraphs objParagraphs = new Models.JSON.JSONParagraphs();

        objParagraphs.contents = objParagraph.ParagraphContent.Replace("\n", " ");
        objParagraphs.sequence = objParagraph.Sequence;
        objParagraphs.character_names = objParagraph.Characters?.Select(x => x.CharacterName).ToArray() ?? Array.Empty<string>();
        objParagraphs.location_name = objParagraph.Location?.LocationName?.Replace("\n", " ") ?? "";
        objParagraphs.timeline_name = objParagraph.Timeline?.TimelineName ?? "";

        return objParagraphs;
    }
    #endregion

    #region public Models.JSON.JSONChapter ConvertToJSONChapter(Chapter paramChapter)
    public Models.JSON.JSONChapter ConvertToJSONChapter(Chapter paramChapter)
    {
        Models.JSON.JSONChapter objChapter = new Models.JSON.JSONChapter();

        objChapter.chapter_name = paramChapter.ChapterName.Replace("\n", " ");
        objChapter.chapter_synopsis = paramChapter.Synopsis.Replace("\n", " ");

        int ParagraphCount = 0;

        if (paramChapter.Paragraph != null)
        {
            ParagraphCount = paramChapter.Paragraph.Count;
        }

        objChapter.paragraphs = new Models.JSON.JSONParagraphs[ParagraphCount];

        for (int i = 0; i < ParagraphCount; i++)
        {
            objChapter.paragraphs[i] = ConvertToJSONParagraph(paramChapter.Paragraph[i]);
        }

        return objChapter;
    }
    #endregion

    #region public Models.JSON.Locations ConvertToJSONLocation(Models.Location objLocation, Paragraph objParagraph)
    public Models.JSON.Locations ConvertToJSONLocation(Models.Location objParamLocation, Paragraph objParagraph)
    {
        Models.JSON.Locations objLocations = new Models.JSON.Locations();

        if (objParamLocation != null)
        {
            objLocations.name = objParamLocation.LocationName.Replace("\n", " ");

            if (objParamLocation.LocationDescription != null)
            {
                objLocations.descriptions = new string[objParamLocation.LocationDescription.Count];
            }
            else
            {
                objLocations.descriptions = new string[0];
            }

            if (objParamLocation.LocationDescription != null)
            {
                int i = 0;
                foreach (var location in objParamLocation.LocationDescription)
                {
                    bool shouldAddDescription = ((objParagraph.Timeline.TimelineName == null || objParagraph.Timeline.TimelineName.Length == 0)
                                    || location.Timeline.TimelineName == objParagraph.Timeline.TimelineName);

                    if (shouldAddDescription)
                    {
                        objLocations.descriptions[i] = location.Description.Replace("\n", " ");
                        i++;
                    }
                }
            }
        }

        return objLocations;
    }
    #endregion

    #region public Models.JSON.Timelines ConvertToJSONTimelines(Timeline objTimeline)
    public Models.JSON.Timelines ConvertToJSONTimelines(Timeline objTimeline)
    {
        Models.JSON.Timelines objTimelines = new Models.JSON.Timelines();

        objTimelines.name = objTimeline.TimelineName.Replace("\n", " ");
        objTimelines.description = objTimeline.TimelineDescription.Replace("\n", " ");

        return objTimelines;
    }
    #endregion
}
