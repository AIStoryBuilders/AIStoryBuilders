using AIStoryBuilders.AI;
using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using static AIStoryBuilders.AI.OrchestratorMethods;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Devices.Sensors;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        public event EventHandler<TextEventArgs> TextEvent;

        private readonly AppMetadata _appMetadata;
        private LogService LogService { get; set; }
        private OrchestratorMethods OrchestratorMethods { get; set; }

        public string BasePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders";
        public AIStoryBuildersService(
            AppMetadata appMetadata,
            LogService _LogService,
            OrchestratorMethods _OrchestratorMethods)
        {
            _appMetadata = appMetadata;
            LogService = _LogService;
            OrchestratorMethods = _OrchestratorMethods;
        }

        // Utility

        #region public string[] ReadCSVFile(string path)
        public string[] ReadCSVFile(string path)
        {
            string[] content;

            // Read the lines from the .csv file
            using (var file = new System.IO.StreamReader(path))
            {
                content = file.ReadToEnd().Split('\n');

                if (content[content.Length - 1].Trim() == "")
                {
                    content = content.Take(content.Length - 1).ToArray();
                }
            }

            var CleanContent = CleanAIStoryBuildersStoriesCSVFile(content);

            return CleanContent;
        }
        #endregion

        #region public string[] CleanAIStoryBuildersStoriesCSVFile(string strAIStoryBuildersStories)
        public string[] CleanAIStoryBuildersStoriesCSVFile(string[] strAIStoryBuildersStories)
        {
            List<string> content = new List<string>();

            // Loop through the lines in strAIStoryBuildersStories
            for (int i = 0; i < strAIStoryBuildersStories.Length; i++)
            {
                // Get line
                var AIStoryBuildersStoriesLine = strAIStoryBuildersStories[i];

                // Split line by |
                var AIStoryBuildersStoriesLineSplit = AIStoryBuildersStoriesLine.Split('|');

                if (AIStoryBuildersStoriesLineSplit[0] != null)
                {
                    if (AIStoryBuildersStoriesLineSplit[0].Length > 0)
                    {
                        // See if AIStoryBuildersStoriesLineSplit[0] is async digit
                        if (AIStoryBuildersStoriesLineSplit[0].All(char.IsDigit))
                        {
                            // See if AIStoryBuildersStoriesLineSplit has 4 segments
                            if (AIStoryBuildersStoriesLineSplit.Length > 3)
                            {
                                // Add line to content
                                content.Add(AIStoryBuildersStoriesLine);
                            }
                        }
                    }
                }
            }

            return content.ToArray();
        }
        #endregion

        #region public void CreateDirectory(string path)
        public void CreateDirectory(string path)
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
        #endregion

        // Knowledge Graph Persistence

        #region private static readonly JsonSerializerOptions GraphJsonOptions
        private static readonly System.Text.Json.JsonSerializerOptions GraphJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
        };
        #endregion

        #region public async Task PersistGraphAsync(Story story, StoryGraph graph, string storyPath)
        public async Task PersistGraphAsync(Story story, StoryGraph graph, string storyPath)
        {
            string graphDir = Path.Combine(storyPath, "Graph");
            CreateDirectory(graphDir);

            // manifest.json
            var manifest = new
            {
                storyTitle = story.Title ?? "",
                createdDate = DateTime.UtcNow.ToString("o"),
                version = _appMetadata.Version,
                nodeCount = graph.Nodes.Count,
                edgeCount = graph.Edges.Count
            };
            string manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest, GraphJsonOptions);
            await File.WriteAllTextAsync(Path.Combine(graphDir, "manifest.json"), manifestJson);

            // graph.json
            string graphJson = System.Text.Json.JsonSerializer.Serialize(graph, GraphJsonOptions);
            await File.WriteAllTextAsync(Path.Combine(graphDir, "graph.json"), graphJson);

            // metadata.json
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
            string metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, GraphJsonOptions);
            await File.WriteAllTextAsync(Path.Combine(graphDir, "metadata.json"), metadataJson);

            LogService.WriteToLog($"Graph persisted for '{story.Title}' — {graph.Nodes.Count} nodes, {graph.Edges.Count} edges");
        }
        #endregion

        #region public StoryGraph LoadGraphFromDisk(string storyPath)
        public StoryGraph LoadGraphFromDisk(string storyPath)
        {
            string graphJsonPath = Path.Combine(storyPath, "Graph", "graph.json");
            if (!File.Exists(graphJsonPath)) return null;

            try
            {
                string json = File.ReadAllText(graphJsonPath);
                return System.Text.Json.JsonSerializer.Deserialize<StoryGraph>(json, GraphJsonOptions);
            }
            catch (Exception ex)
            {
                LogService.WriteToLog($"LoadGraphFromDisk failed: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region public async Task EnsureGraphExistsAsync(string storyTitle, IGraphBuilder graphBuilder)
        public async Task EnsureGraphExistsAsync(string storyTitle, IGraphBuilder graphBuilder)
        {
            string storyPath = $"{BasePath}/{storyTitle}";
            string graphJsonPath = Path.Combine(storyPath, "Graph", "graph.json");

            if (File.Exists(graphJsonPath))
            {
                // Load existing graph from disk
                var existingGraph = LoadGraphFromDisk(storyPath);
                if (existingGraph != null)
                {
                    GraphState.Current = existingGraph;
                    // Load the Story object for GraphState.CurrentStory
                    var stories = GetStorys();
                    var storyMeta = stories.FirstOrDefault(s => s.Title == storyTitle);
                    if (storyMeta != null)
                    {
                        GraphState.CurrentStory = LoadFullStory(storyMeta);
                    }
                    return;
                }
            }

            // Build graph from disk files
            var allStories = GetStorys();
            var story = allStories.FirstOrDefault(s => s.Title == storyTitle);
            if (story == null)
            {
                LogService.WriteToLog($"EnsureGraphExistsAsync: Story '{storyTitle}' not found in stories list");
                return;
            }

            var fullStory = LoadFullStory(story);
            var graph = graphBuilder.Build(fullStory);
            await PersistGraphAsync(fullStory, graph, storyPath);

            GraphState.Current = graph;
            GraphState.CurrentStory = fullStory;

            LogService.WriteToLog($"EnsureGraphExistsAsync: Graph built and persisted for '{storyTitle}'");
        }
        #endregion

        #region public Story LoadFullStory(Story storyMeta)
        public Story LoadFullStory(Story storyMeta)
        {
            // Populate story-level metadata from CSV
            var allStories = GetStorys();
            var csvStory = allStories.FirstOrDefault(s =>
                s.Title.Equals(storyMeta.Title, StringComparison.OrdinalIgnoreCase));
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
        #endregion

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
}
