using AIStoryBuilders.AI;
using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using static AIStoryBuilders.AI.OrchestratorMethods;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System.Linq;

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

            return content;
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

        #region public string GetOnlyJSON(string json)
        public string GetOnlyJSON(string json)
        {
            string OnlyJSON = "";
            // Search for the first occurrence of the { character
            int FirstCurlyBrace = json.IndexOf('{');
            // Set ParsedStory to the string after the first occurrence of the { character
            OnlyJSON = json.Substring(FirstCurlyBrace);

            return OnlyJSON;
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
                    bool shouldAddDescription = objParagraph.Timeline.TimelineName == null ||
                                                objParagraph.Timeline.TimelineName.Length == 0 ||
                                                background.Timeline.TimelineName == objParagraph.Timeline.TimelineName ||
                                                string.IsNullOrEmpty(background.Timeline.TimelineName);

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
        public Models.JSON.Paragraphs ConvertToJSONParagraph(Paragraph objParagraph)
        {
            Models.JSON.Paragraphs objParagraphs = new Models.JSON.Paragraphs();

            objParagraphs.contents = objParagraph.ParagraphContent.Replace("\n", " ");
            objParagraphs.sequence = objParagraph.Sequence;
            objParagraphs.character_names = objParagraph.Characters.Select(x => x.CharacterName).ToArray();
            objParagraphs.location_name = objParagraph.Location.LocationName;
            objParagraphs.timeline_name = objParagraph.Timeline.TimelineName;

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

            objChapter.paragraphs = new Models.JSON.Paragraphs[ParagraphCount];

            for (int i = 0; i < ParagraphCount; i++)
            {
                objChapter.paragraphs[i] = ConvertToJSONParagraph(paramChapter.Paragraph[i]);
            }

            return objChapter;
        }
        #endregion

        #region public Models.JSON.Locations ConvertToJSONLocation(Models.Location objLocation)
        public Models.JSON.Locations ConvertToJSONLocation(Models.Location objLocation)
        {
            Models.JSON.Locations objLocations = new Models.JSON.Locations();

            objLocations.name = objLocation.LocationName.Replace("\n", " ");

            if (objLocation.LocationDescription != null)
            {
                objLocations.descriptions = new string[objLocation.LocationDescription.Count];
            }
            else
            {
                objLocations.descriptions = new string[0];
            }

            if (objLocation.LocationDescription != null)
            {
                int i = 0;
                foreach (var location in objLocation.LocationDescription)
                {
                    objLocations.descriptions[i] = location.Description;
                    i++;
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
