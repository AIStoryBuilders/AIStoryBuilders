using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using static AIStoryBuilders.AI.OrchestratorMethods;
using Character = AIStoryBuilders.Models.Character;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region *** Story ***
        public List<Story> GetStorys()
        {
            var AIStoryBuildersStoriesPath = $"{BasePath}/AIStoryBuildersStories.csv";
            string[] AIStoryBuildersStoriesContent = ReadCSVFile(AIStoryBuildersStoriesPath);

            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Trim() != "").ToArray();

            try
            {
                // Return collection of Story
                return AIStoryBuildersStoriesContent
                    .Select(story => story.Split('|'))
                    .Select(story => new Story
                    {
                        Id = int.Parse(story[0]),
                        Title = story[1],
                        Style = story[2],
                        Theme = story[3],
                        Synopsis = story[4],
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("GetStorys: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return new List<Story>();
            }
        }

        public async Task AddStory(Story story, string GPTModelId)
        {
            // Create Characters, Chapters, Timelines, and Locations sub folders

            string StoryPath = $"{BasePath}/{story.Title}";
            string CharactersPath = $"{StoryPath}/Characters";
            string ChaptersPath = $"{StoryPath}/Chapters";
            string LocationsPath = $"{StoryPath}/Locations";

            //  ********** Call the LLM to Parse the Story to create the files **********
            OpenAI.Chat.Message ParsedStoryJSON = await OrchestratorMethods.ParseNewStory(story.Title, story.Synopsis, GPTModelId);

            CreateDirectory(StoryPath);
            CreateDirectory(CharactersPath);
            CreateDirectory(ChaptersPath);
            CreateDirectory(LocationsPath);

            // Add Story to file
            var AIStoryBuildersStoriesPath = $"{BasePath}/AIStoryBuildersStories.csv";
            string[] AIStoryBuildersStoriesContent = ReadCSVFile(AIStoryBuildersStoriesPath);

            // Remove all empty lines
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Trim() != "").ToArray();

            // Trim all lines
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Select(line => line.Trim()).ToArray();

            // Add Story to file
            string newStory = $"{AIStoryBuildersStoriesContent.Count() + 1}|{story.Title}|{story.Style}|{story.Theme}|{story.Synopsis}";
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Append(newStory).ToArray();
            File.WriteAllLines(AIStoryBuildersStoriesPath, AIStoryBuildersStoriesContent);

            // Log
            LogService.WriteToLog($"Story created {story.Title}");

            JSONStory ParsedNewStory = new JSONStory();

            // Convert the JSON to a dynamic object
            ParsedNewStory = ParseJSONNewStory(ParsedStoryJSON.Content.ToString());

            // *****************************************************

            // Create the Character files
            TextEvent?.Invoke(this, new TextEventArgs($"Create the Character files", 5));
            foreach (var character in ParsedNewStory.characters)
            {
                // Add Character to file
                string CharacterName = OrchestratorMethods.SanitizeFileName(character.name);

                // Create Character file
                string CharacterPath = $"{CharactersPath}/{CharacterName}.csv";
                List<string> CharacterContents = new List<string>();

                foreach (var description in character.descriptions)
                {
                    string description_type = description.description_type ?? "";
                    string timeline_name = description.timeline_name ?? "";
                    string VectorDescriptionAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(description.description ?? "", true);
                    CharacterContents.Add($"{description_type}|{timeline_name}|{VectorDescriptionAndEmbedding}" + Environment.NewLine);
                }

                File.WriteAllLines(CharacterPath, CharacterContents);
            }

            // Create the Location files
            TextEvent?.Invoke(this, new TextEventArgs($"Create the Location files", 5));
            foreach (var location in ParsedNewStory.locations)
            {
                // Add Location to file
                string LocationName = OrchestratorMethods.SanitizeFileName(location.name);

                // Create Location file
                string LocationPath = $"{LocationsPath}/{LocationName}.csv";
                List<string> LocationContents = new List<string>();

                if (location.descriptions != null)
                {
                    foreach (var description in location.descriptions)
                    {
                        string VectorEmbedding = await OrchestratorMethods.GetVectorEmbedding(description, false);

                        // We are deliberately not setting a LocationTimeline (therefore setting it to empty string)
                        // We did not ask the AI to set this value because it would have ben asking too much
                        var LocationDescriptionAndTimeline = $"{description}|";
                        LocationContents.Add($"{LocationDescriptionAndTimeline}|{VectorEmbedding}" + Environment.NewLine);
                    }
                }
                else
                {
                    string VectorEmbedding = await OrchestratorMethods.GetVectorEmbedding(location.name, false);

                    // We are deliberately not setting a LocationTimeline (therefore setting it to empty string)
                    // We did not ask the AI to set this value because it would have ben asking too much
                    var LocationDescriptionAndTimeline = $"{location.name}|";
                    LocationContents.Add($"{LocationDescriptionAndTimeline}|{VectorEmbedding}" + Environment.NewLine);
                }

                File.WriteAllLines(LocationPath, LocationContents);
            }

            // Create the Timeline file
            TextEvent?.Invoke(this, new TextEventArgs($"Create the Timeline file", 5));
            List<string> TimelineContents = new List<string>();

            int i = 0;
            foreach (var timeline in ParsedNewStory.timelines)
            {
                // Add Timeline to file
                string TimelineName = OrchestratorMethods.SanitizeFileName(timeline.name);

                string StartTime = DateTime.Now.AddDays(i).ToShortDateString() + " " + DateTime.Now.AddDays(i).ToShortTimeString();
                string StopTime = DateTime.Now.AddDays(i + 1).ToShortDateString() + " " + DateTime.Now.AddDays(i + 1).ToShortTimeString();
                string TimelineContentsLine = $"{TimelineName}|{timeline.description}|{StartTime}|{StopTime}";

                TimelineContents.Add(TimelineContentsLine);
                i = i + 2;
            }

            string TimelinePath = $"{StoryPath}/Timelines.csv";
            File.WriteAllLines(TimelinePath, TimelineContents);

            //// **** Create the First Paragraph and the Chapters

            // Call ChatGPT
            OpenAI.Chat.Message ParsedChaptersJSON = await OrchestratorMethods.CreateNewChapters(ParsedStoryJSON, story.ChapterCount, GPTModelId);

            JSONChapters ParsedNewChapters = new JSONChapters();

            // Convert the JSON to a dynamic object
            ParsedNewChapters = ParseJSONNewChapters(GetOnlyJSON(ParsedChaptersJSON.Content.ToString()));

            // Test to see that something was returned
            if (ParsedNewChapters.chapter.Length == 0)
            {
                // Clean the JSON
                ParsedChaptersJSON = await OrchestratorMethods.CleanJSON(GetOnlyJSON(ParsedChaptersJSON.Content.ToString()), GPTModelId);

                // Convert the JSON to a dynamic object
                ParsedNewChapters = ParseJSONNewChapters(GetOnlyJSON(ParsedChaptersJSON.Content.ToString()));
            }

            //// **** Create the Files

            int ChapterNumber = 1;
            foreach (var chapter in ParsedNewChapters.chapter)
            {
                // Create a folder in Chapters/
                string ChapterPath = $"{ChaptersPath}/Chapter{ChapterNumber}";
                CreateDirectory(ChapterPath);

                TextEvent?.Invoke(this, new TextEventArgs($"Create Chapter {ChapterNumber}", 5));

                if (chapter.chapter_synopsis != null)
                {
                    // Create a file at: Chapters/Chapter{ChapterNumber}/Chapter{ChapterNumber}.txt
                    string ChapterFilePath = $"{ChapterPath}/Chapter{ChapterNumber}.txt";
                    string ChapterSynopsisAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(chapter.chapter_synopsis, true);
                    File.WriteAllText(ChapterFilePath, $"{ChapterSynopsisAndEmbedding}");

                    if (chapter.paragraphs[0] != null)
                    {
                        // Create a file at: Chapters/Chapter1/Paragraph1.txt
                        string FirstParagraphPath = $"{ChapterPath}/Paragraph1.txt";
                        string VectorDescriptionAndEmbeddingFirstParagraph = await OrchestratorMethods.GetVectorEmbedding(chapter.paragraphs[0].contents, true);

                        // Only allow one Location and Timeline
                        var TempLocation = chapter.paragraphs[0].location_name;
                        var TempTimeline = chapter.paragraphs[0].timeline_name;

                        // Split the Location and Timeline using the comma
                        var TempLocationSplit = TempLocation.Split(',');
                        var TempTimelineSplit = TempTimeline.Split(',');

                        // Get the first Location and Timeline
                        string Location = TempLocationSplit[0];
                        string Timeline = TempTimelineSplit[0];

                        string Characters = "[";

                        if (chapter.paragraphs[0].character_names != null)
                        {
                            foreach (var character in chapter.paragraphs[0].character_names)
                            {
                                Characters += $"{character},";
                            }

                            // Remove the last comma
                            Characters = Characters.Remove(Characters.Length - 1);

                        }
                        Characters = Characters + "]";

                        File.WriteAllText(FirstParagraphPath, $"{Location}|{Timeline}|{Characters}|{VectorDescriptionAndEmbeddingFirstParagraph}");
                    }
                }

                ChapterNumber++;
            }
        }

        public void UpdateStory(Story story)
        {
            // Get all Stories from file
            var AIStoryBuildersStoriesPath = $"{BasePath}/AIStoryBuildersStories.csv";
            string[] AIStoryBuildersStoriesContent = ReadCSVFile(AIStoryBuildersStoriesPath);

            // Remove all empty lines
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Trim() != "").ToArray();

            // Get all lines except the one to update
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Split('|')[1] != story.Title).ToArray();

            // Trim all lines
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Select(line => line.Trim()).ToArray();

            // Remove any line breaks
            story.Style = RemoveLineBreaks(story.Style);
            story.Theme = RemoveLineBreaks(story.Theme);
            story.Synopsis = RemoveLineBreaks(story.Synopsis);

            // Remove any pipes (because that is used as a delimiter)
            story.Style = story.Style.Replace("|", "");
            story.Theme = story.Theme.Replace("|", "");
            story.Synopsis = story.Synopsis.Replace("|", "");

            // Re-add Story to file
            string updatedStory = $"{AIStoryBuildersStoriesContent.Count() + 1}|{story.Title}|{story.Style}|{story.Theme}|{story.Synopsis}";
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Append(updatedStory).ToArray();
            File.WriteAllLines(AIStoryBuildersStoriesPath, AIStoryBuildersStoriesContent);
        }

        public void DeleteStory(string StoryTitle)
        {
            // Get Story from file
            var AIStoryBuildersStoriesPath = $"{BasePath}/AIStoryBuildersStories.csv";
            string[] AIStoryBuildersStoriesContent = ReadCSVFile(AIStoryBuildersStoriesPath);

            // Remove all empty lines
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Trim() != "").ToArray();

            // Remove Story from file
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Split('|')[1] != StoryTitle).ToArray();
            File.WriteAllLines(AIStoryBuildersStoriesPath, AIStoryBuildersStoriesContent);

            // Delete folder and all its sub folders and files
            string StoryPath = $"{BasePath}/{StoryTitle}";
            Directory.Delete(StoryPath, true);

            // Log
            LogService.WriteToLog($"Story deleted {StoryTitle}");
        }
        #endregion

        #region *** Timelines ***
        public List<AIStoryBuilders.Models.Timeline> GetTimelines(Story story)
        {
            // Create a collection of Timelines
            List<AIStoryBuilders.Models.Timeline> Timelines = new List<AIStoryBuilders.Models.Timeline>();

            var AIStoryBuildersTimelinesPath = $"{BasePath}/{story.Title}/Timelines.csv";

            try
            {
                // load the Timelines file
                string[] AIStoryBuildersTimelinesContent = File.ReadAllLines(AIStoryBuildersTimelinesPath);

                // Remove all empty lines
                AIStoryBuildersTimelinesContent = AIStoryBuildersTimelinesContent.Where(line => line.Trim() != "").ToArray();

                // Loop through each Timeline line
                int i = 1;
                foreach (var AIStoryBuildersTimelineLine in AIStoryBuildersTimelinesContent)
                {
                    // Get the TimelineName from the line
                    string[] AIStoryBuildersTimelineLineSplit = AIStoryBuildersTimelineLine.Split('|');
                    string TimelineName = AIStoryBuildersTimelineLineSplit[0];

                    // Get the TimelineDescription from the line
                    string TimelineDescription = AIStoryBuildersTimelineLineSplit[1];

                    // Get the TimelineStartTime from the line
                    string TimelineStartTime = AIStoryBuildersTimelineLineSplit[2];

                    // Get the TimelineStopTime from the line
                    string TimelineStopTime = AIStoryBuildersTimelineLineSplit[3];

                    // Create a Timeline
                    AIStoryBuilders.Models.Timeline Timeline = new AIStoryBuilders.Models.Timeline();
                    Timeline.Id = i;
                    Timeline.TimelineName = TimelineName;
                    Timeline.TimelineDescription = TimelineDescription;
                    Timeline.StartDate = DateTime.Parse(TimelineStartTime);

                    // use tryparse to try to parse TimelineStopTime
                    DateTime TimelineStopDate;
                    DateTime.TryParse(TimelineStopTime, out TimelineStopDate);
                    if (TimelineStopDate != DateTime.MinValue)
                    {
                        Timeline.StopDate = DateTime.Parse(TimelineStopTime);
                    }

                    // Add Timeline to collection
                    Timelines.Add(Timeline);

                    i++;
                }

                // Return collection of Timelines
                return Timelines.OrderBy(x => x.StartDate).ToList();
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("GetTimelines: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return new List<AIStoryBuilders.Models.Timeline>();
            }
        }

        public void AddTimeline(Models.Timeline objTimeline)
        {
            try
            {
                string StoryPath = $"{BasePath}/{objTimeline.Story.Title}";
                string TimelinesPath = $"{StoryPath}/Timelines.csv";

                // Add Timeline to file
                string TimelineContents = $"{objTimeline.TimelineName}|{objTimeline.TimelineDescription}|{objTimeline.StartDate}|{objTimeline.StopDate}" + Environment.NewLine;
                File.AppendAllText(TimelinesPath, TimelineContents);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("AddTimeline: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public void UpdateTimeline(Models.Timeline objTimeline, string paramTimelineNameOriginal)
        {
            try
            {
                // Get all Timelines from file
                var ExistingTimelines = GetTimelines(objTimeline.Story);

                // Get all Timelines except the one to update
                ExistingTimelines = ExistingTimelines.Where(line => line.TimelineName != paramTimelineNameOriginal).ToList();

                // Add the updated Timeline - first update it to use the paramTimelineNameOriginal (in case it was changed)
                objTimeline.TimelineName = paramTimelineNameOriginal;
                ExistingTimelines.Add(objTimeline);

                // Create the lines to write to the Timeline file
                List<string> TimelineContents = new List<string>();

                foreach (var timeline in ExistingTimelines)
                {
                    string StartTime = timeline.StartDate.Value.ToShortDateString() + " " + timeline.StartDate.Value.ToShortTimeString();

                    string StopTime = "";

                    if (timeline.StopDate.HasValue)
                    {
                        StopTime = timeline.StopDate.Value.ToShortDateString() + " " + timeline.StopDate.Value.ToShortTimeString();
                    }

                    string TimelineContentsLine = $"{timeline.TimelineName}|{timeline.TimelineDescription}|{StartTime}|{StopTime}";
                    TimelineContents.Add(TimelineContentsLine);
                }

                // Write the file
                string StoryPath = $"{BasePath}/{objTimeline.Story.Title}";
                string TimelinesPath = $"{StoryPath}/Timelines.csv";
                File.WriteAllLines(TimelinesPath, TimelineContents);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("UpdateTimeline: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public async Task UpdateTimelineAndTimelineNameAsync(Models.Timeline objTimeline, string paramTimelineNameOriginal)
        {
            try
            {
                // ********************************************************
                // Update in Timeline.csv file
                // ********************************************************

                // Get all Timelines from file
                var ExistingTimelines = GetTimelines(objTimeline.Story);

                // Get all Timelines except the one to update
                ExistingTimelines = ExistingTimelines.Where(line => line.TimelineName != paramTimelineNameOriginal).ToList();

                // Add the updated Timeline - It will have the updated name
                ExistingTimelines.Add(objTimeline);

                // Create the lines to write to the Timeline file
                List<string> TimelineContents = new List<string>();

                foreach (var timeline in ExistingTimelines)
                {
                    string StartTime = timeline.StartDate.Value.ToShortDateString() + " " + timeline.StartDate.Value.ToShortTimeString();

                    string StopTime = "";

                    if (timeline.StopDate.HasValue)
                    {
                        StopTime = timeline.StopDate.Value.ToShortDateString() + " " + timeline.StopDate.Value.ToShortTimeString();
                    }

                    string TimelineContentsLine = $"{timeline.TimelineName}|{timeline.TimelineDescription}|{StartTime}|{StopTime}";
                    TimelineContents.Add(TimelineContentsLine);
                }

                // Write the file
                string StoryPath = $"{BasePath}/{objTimeline.Story.Title}";
                string TimelinesPath = $"{StoryPath}/Timelines.csv";
                File.WriteAllLines(TimelinesPath, TimelineContents);

                // ********************************************************
                // Update Chapter files
                // ********************************************************

                // Loops through every Chapter and Paragraph 
                var Chapters = GetChapters(objTimeline.Story);

                foreach (var Chapter in Chapters)
                {
                    var Paragraphs = GetParagraphs(Chapter);

                    foreach (var Paragraph in Paragraphs)
                    {
                        // Create the path to the Paragraph file
                        var ChapterNameParts = Chapter.ChapterName.Split(' ');
                        string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];
                        string ParagraphPath = $"{StoryPath}/Chapters/{ChapterName}/Paragraph{Paragraph.Sequence}.txt";

                        // Get the ParagraphContent from the file
                        string[] ParagraphContent = File.ReadAllLines(ParagraphPath);

                        // Remove all empty lines
                        ParagraphContent = ParagraphContent.Where(line => line.Trim() != "").ToArray();

                        // Get the Timeline from the file
                        string[] ParagraphTimeline = ParagraphContent[0].Split('|');

                        // If the Location is the one to update, then set it to new name
                        if (ParagraphTimeline[1] == paramTimelineNameOriginal)
                        {
                            // Set to the new name
                            ParagraphTimeline[1] = objTimeline.TimelineName;

                            // Put the ParagraphContent back together
                            ParagraphContent[0] = string.Join("|", ParagraphTimeline);

                            // Write the ParagraphContent back to the file
                            File.WriteAllLines(ParagraphPath, ParagraphContent);
                        }
                    }
                }

                // ********************************************************
                // Update Location files
                // ********************************************************

                string LocationsPath = $"{StoryPath}/Locations";
                List<AIStoryBuilders.Models.Location> Locations = GetLocations(objTimeline.Story);

                // Loop through each Location file
                foreach (var AIStoryBuildersLocation in Locations)
                {
                    List<string> LocationContents = new List<string>();

                    foreach (var LocationDescription in AIStoryBuildersLocation.LocationDescription)
                    {
                        string LocationDescriptionAndTimeline = "";

                        // Does the TimelineName element exist?
                        if (LocationDescription.Timeline != null)
                        {
                            // Is the TimelineName the one to update?
                            if (LocationDescription.Timeline.TimelineName == paramTimelineNameOriginal)
                            {
                                // Update to new name
                                LocationDescriptionAndTimeline = $"{LocationDescription.Description}|{objTimeline.TimelineName}";
                            }
                            else
                            {
                                // Use existing values
                                LocationDescriptionAndTimeline = $"{LocationDescription.Description}|{LocationDescription.Timeline.TimelineName}";
                            }
                        }
                        else
                        {
                            // Use existing values - No TimelineName
                            LocationDescriptionAndTimeline = $"{LocationDescription.Description}|";
                        }

                        string VectorEmbedding = await OrchestratorMethods.GetVectorEmbedding(LocationDescription.Description, false);

                        LocationContents.Add($"{LocationDescriptionAndTimeline}|{VectorEmbedding}" + Environment.NewLine);
                    }

                    string LocationPath = $"{LocationsPath}/{AIStoryBuildersLocation.LocationName}.csv";
                    File.WriteAllLines(LocationPath, LocationContents);
                }

                // ********************************************************
                // Update Character files
                // ********************************************************

                string CharactersPath = $"{StoryPath}/Characters";
                List<AIStoryBuilders.Models.Character> Characters = GetCharacters(objTimeline.Story);

                // Loop through each Character file
                foreach (var AIStoryBuildersCharacter in Characters)
                {
                    List<string> CharacterContents = new List<string>();

                    foreach (var CharacterDescription in AIStoryBuildersCharacter.CharacterBackground)
                    {
                        string CharacterDescriptionAndTimeline = "";

                        // Does the TimelineName element exist?
                        if (CharacterDescription.Timeline != null)
                        {
                            // Is the TimelineName the one to update?
                            if (CharacterDescription.Timeline.TimelineName == paramTimelineNameOriginal)
                            {
                                // Update to new name
                                CharacterDescriptionAndTimeline = $"{CharacterDescription.Type}|{objTimeline.TimelineName}|{CharacterDescription.Description}";
                            }
                            else
                            {
                                // Use existing values
                                CharacterDescriptionAndTimeline = $"{CharacterDescription.Type}|{CharacterDescription.Timeline.TimelineName}|{CharacterDescription.Description}";
                            }
                        }
                        else
                        {
                            // Use existing values - No TimelineName
                            CharacterDescriptionAndTimeline = $"{CharacterDescription.Type}||{CharacterDescription.Description}";
                        }

                        string VectorEmbedding = await OrchestratorMethods.GetVectorEmbedding(CharacterDescription.Description, false);

                        CharacterContents.Add($"{CharacterDescriptionAndTimeline}|{VectorEmbedding}" + Environment.NewLine);
                    }

                    string CharacterPath = $"{CharactersPath}/{AIStoryBuildersCharacter.CharacterName}.csv";
                    File.WriteAllLines(CharacterPath, CharacterContents);
                }
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("UpdateTimelineAndTimelineNameAsync: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public async Task DeleteTimelineAndTimelineNameAsync(Models.Timeline objTimeline, string paramTimelineNameOriginal)
        {
            try
            {
                // ********************************************************
                // Update in Timeline.csv file
                // ********************************************************

                // Get all Timelines from file
                var ExistingTimelines = GetTimelines(objTimeline.Story);

                // Get all Timelines except the one to update
                ExistingTimelines = ExistingTimelines.Where(line => line.TimelineName != paramTimelineNameOriginal).ToList();

                // Create the lines to write to the Timeline file
                List<string> TimelineContents = new List<string>();

                // Write all existing lines to the file
                // This will delete the Timeline
                foreach (var timeline in ExistingTimelines)
                {
                    string StartTime = timeline.StartDate.Value.ToShortDateString() + " " + timeline.StartDate.Value.ToShortTimeString();

                    string StopTime = "";

                    if (timeline.StopDate.HasValue)
                    {
                        StopTime = timeline.StopDate.Value.ToShortDateString() + " " + timeline.StopDate.Value.ToShortTimeString();
                    }

                    string TimelineContentsLine = $"{timeline.TimelineName}|{timeline.TimelineDescription}|{StartTime}|{StopTime}";
                    TimelineContents.Add(TimelineContentsLine);
                }

                // Write the file
                string StoryPath = $"{BasePath}/{objTimeline.Story.Title}";
                string TimelinesPath = $"{StoryPath}/Timelines.csv";
                File.WriteAllLines(TimelinesPath, TimelineContents);

                // ********************************************************
                // Update Chapter files
                // ********************************************************

                // Loops through every Chapter and Paragraph 
                var Chapters = GetChapters(objTimeline.Story);

                foreach (var Chapter in Chapters)
                {
                    var Paragraphs = GetParagraphs(Chapter);

                    foreach (var Paragraph in Paragraphs)
                    {
                        // Create the path to the Paragraph file
                        var ChapterNameParts = Chapter.ChapterName.Split(' ');
                        string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];
                        string ParagraphPath = $"{StoryPath}/Chapters/{ChapterName}/Paragraph{Paragraph.Sequence}.txt";

                        // Get the ParagraphContent from the file
                        string[] ParagraphContent = File.ReadAllLines(ParagraphPath);

                        // Remove all empty lines
                        ParagraphContent = ParagraphContent.Where(line => line.Trim() != "").ToArray();

                        // Get the Timeline from the file
                        string[] ParagraphTimeline = ParagraphContent[0].Split('|');

                        // If the Timeline is the one to remove, then set it to a space
                        if (ParagraphTimeline[1] == paramTimelineNameOriginal)
                        {
                            // Set to nothing
                            ParagraphTimeline[1] = "";

                            // Put the ParagraphContent back together
                            ParagraphContent[0] = string.Join("|", ParagraphTimeline);

                            // Write the ParagraphContent back to the file
                            File.WriteAllLines(ParagraphPath, ParagraphContent);
                        }
                    }
                }

                // ********************************************************
                // Update Location files
                // ********************************************************

                string LocationsPath = $"{StoryPath}/Locations";
                List<AIStoryBuilders.Models.Location> Locations = GetLocations(objTimeline.Story);

                // Loop through each Location file
                foreach (var AIStoryBuildersLocation in Locations)
                {
                    List<string> LocationContents = new List<string>();

                    foreach (var LocationDescription in AIStoryBuildersLocation.LocationDescription)
                    {
                        string LocationDescriptionAndTimeline = "";

                        // Does the TimelineName element exist?
                        if (LocationDescription.Timeline != null)
                        {
                            // Is the TimelineName the one to update?
                            if (LocationDescription.Timeline.TimelineName == paramTimelineNameOriginal)
                            {
                                // Update to nothing
                                LocationDescriptionAndTimeline = $"{LocationDescription.Description}|";
                            }
                            else
                            {
                                // Use existing values
                                LocationDescriptionAndTimeline = $"{LocationDescription.Description}|{LocationDescription.Timeline.TimelineName}";
                            }
                        }
                        else
                        {
                            // Use existing values - No TimelineName
                            LocationDescriptionAndTimeline = $"{LocationDescription.Description}|";
                        }

                        string VectorEmbedding = await OrchestratorMethods.GetVectorEmbedding(LocationDescription.Description, false);

                        LocationContents.Add($"{LocationDescriptionAndTimeline}|{VectorEmbedding}" + Environment.NewLine);
                    }

                    string LocationPath = $"{LocationsPath}/{AIStoryBuildersLocation.LocationName}.csv";
                    File.WriteAllLines(LocationPath, LocationContents);
                }

                // ********************************************************
                // Update Character files
                // ********************************************************

                string CharactersPath = $"{StoryPath}/Characters";
                List<AIStoryBuilders.Models.Character> Characters = GetCharacters(objTimeline.Story);

                // Loop through each Character file
                foreach (var AIStoryBuildersCharacter in Characters)
                {
                    List<string> CharacterContents = new List<string>();

                    foreach (var CharacterDescription in AIStoryBuildersCharacter.CharacterBackground)
                    {
                        string CharacterDescriptionAndTimeline = "";

                        // Does the TimelineName element exist?
                        if (CharacterDescription.Timeline != null)
                        {
                            // Is the TimelineName the one to update?
                            if (CharacterDescription.Timeline.TimelineName == paramTimelineNameOriginal)
                            {
                                // Update to remove the TimelineName
                                CharacterDescriptionAndTimeline = $"{CharacterDescription.Type}||{CharacterDescription.Description}";
                            }
                            else
                            {
                                // Use existing values
                                CharacterDescriptionAndTimeline = $"{CharacterDescription.Type}|{CharacterDescription.Timeline.TimelineName}|{CharacterDescription.Description}";
                            }
                        }
                        else
                        {
                            // Use existing values - No TimelineName
                            CharacterDescriptionAndTimeline = $"{CharacterDescription.Type}||{CharacterDescription.Description}";
                        }

                        string VectorEmbedding = await OrchestratorMethods.GetVectorEmbedding(CharacterDescription.Description, false);

                        CharacterContents.Add($"{CharacterDescriptionAndTimeline}|{VectorEmbedding}" + Environment.NewLine);
                    }

                    string CharacterPath = $"{CharactersPath}/{AIStoryBuildersCharacter.CharacterName}.csv";
                    File.WriteAllLines(CharacterPath, CharacterContents);
                }
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("DeleteTimelineAndTimelineNameAsync: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }
        #endregion

        #region *** Locations ***
        public List<AIStoryBuilders.Models.Location> GetLocations(Story story)
        {
            // Create a collection of Location
            List<AIStoryBuilders.Models.Location> Locations = new List<AIStoryBuilders.Models.Location>();

            var AIStoryBuildersLocationsPath = $"{BasePath}/{story.Title}/Locations";

            try
            {
                // Get a list of all the Location files
                string[] AIStoryBuildersLocationsFiles = Directory.GetFiles(AIStoryBuildersLocationsPath, "*.csv", SearchOption.AllDirectories);

                // Loop through each Location file
                int i = 1;
                foreach (var AIStoryBuildersLocationFile in AIStoryBuildersLocationsFiles)
                {
                    // Get the LocationName from the file name
                    string LocationName = Path.GetFileNameWithoutExtension(AIStoryBuildersLocationFile);

                    // Get the LocationContent from the file
                    string[] LocationContent = File.ReadAllLines(AIStoryBuildersLocationFile);

                    // Remove all empty lines
                    LocationContent = LocationContent.Where(line => line.Trim() != "").ToArray();

                    var LocationDescriptionRaw = LocationContent.Select(x => x.Split('|')).ToArray();

                    // Create a Location
                    AIStoryBuilders.Models.Location Location = new AIStoryBuilders.Models.Location();
                    Location.Id = i;
                    Location.LocationName = LocationName;
                    Location.LocationDescription = new List<LocationDescription>();

                    if (LocationDescriptionRaw.Count() > 0)
                    {
                        int ii = 1;
                        foreach (var description in LocationDescriptionRaw)
                        {
                            var DescriptionRaw = description.Select(x => x.Split('|')).ToArray();

                            LocationDescription objLocationDescription = new LocationDescription();
                            objLocationDescription.Id = ii;
                            objLocationDescription.Description = DescriptionRaw[0][0];

                            // Does the TimelineName element exist?
                            if (DescriptionRaw[1].Count() > 0)
                            {
                                Models.Timeline objTimeline = new Models.Timeline();
                                objTimeline.TimelineName = DescriptionRaw[1][0];

                                objLocationDescription.Timeline = objTimeline;
                            }

                            Location.LocationDescription.Add(objLocationDescription);
                            ii++;
                        }
                    }

                    // Add Location to collection
                    Locations.Add(Location);
                    i++;
                }

                // Return collection of Locations
                return Locations;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("GetLocations: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return new List<AIStoryBuilders.Models.Location>();
            }
        }

        public bool LocationExists(Models.Location objLocation)
        {
            bool LocationExists = true;
            var AIStoryBuildersLocationsPath = $"{BasePath}/{objLocation.Story.Title}/Locations";

            try
            {
                // Get a list of all the Location files
                string[] AIStoryBuildersLocationsFiles = Directory.GetFiles(AIStoryBuildersLocationsPath, "*.csv", SearchOption.AllDirectories);

                List<string> ExistingLocations = new List<string>();
                // Loop through each Location file
                foreach (var AIStoryBuildersLocationFile in AIStoryBuildersLocationsFiles)
                {
                    // Get the LocationName from the file name
                    string LocationName = Path.GetFileNameWithoutExtension(AIStoryBuildersLocationFile);

                    ExistingLocations.Add(LocationName.ToLower());
                }

                if (ExistingLocations.Contains(objLocation.LocationName.ToLower()))
                {
                    LocationExists = true;
                }
                else
                {
                    LocationExists = false;
                }

                return LocationExists;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("LocationExists: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return true;
            }
        }

        public async Task AddLocationAsync(Models.Location objLocation)
        {
            try
            {
                string StoryPath = $"{BasePath}/{objLocation.Story.Title}";
                string LocationsPath = $"{StoryPath}/Locations";

                // Add Location to file
                List<string> LocationContents = new List<string>();
                string LocationName = OrchestratorMethods.SanitizeFileName(objLocation.LocationName);

                foreach (var description in objLocation.LocationDescription)
                {
                    string VectorEmbedding = await OrchestratorMethods.GetVectorEmbedding(description.Description, false);

                    // Set TimelineName to empty string if null
                    string TimelineName = "";
                    if (description.Timeline == null)
                    {
                        TimelineName = "";
                    }
                    else
                    {
                        TimelineName = description.Timeline.TimelineName ?? "";
                    }

                    var LocationDescriptionAndTimeline = $"{description.Description}|{TimelineName}";
                    LocationContents.Add($"{LocationDescriptionAndTimeline}|{VectorEmbedding}" + Environment.NewLine);
                }

                string LocationPath = $"{LocationsPath}/{LocationName}.csv";
                File.WriteAllLines(LocationPath, LocationContents);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("AddLocationAsync: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public async Task UpdateLocationDescriptions(Models.Location objLocation)
        {
            try
            {
                string StoryPath = $"{BasePath}/{objLocation.Story.Title}";
                string LocationsPath = $"{StoryPath}/Locations";

                // Add Location to file
                List<string> LocationContents = new List<string>();
                string LocationName = OrchestratorMethods.SanitizeFileName(objLocation.LocationName);

                foreach (var description in objLocation.LocationDescription)
                {
                    string VectorEmbedding = await OrchestratorMethods.GetVectorEmbedding(description.Description, false);

                    // Set TimelineName to empty string if null
                    string TimelineName = "";
                    if (description.Timeline == null)
                    {
                        TimelineName = "";
                    }
                    else
                    {
                        TimelineName = description.Timeline.TimelineName ?? "";
                    }

                    var LocationDescriptionAndTimeline = $"{description.Description}|{TimelineName}";
                    LocationContents.Add($"{LocationDescriptionAndTimeline}|{VectorEmbedding}" + Environment.NewLine);
                }

                string LocationPath = $"{LocationsPath}/{LocationName}.csv";
                File.WriteAllLines(LocationPath, LocationContents);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("UpdateLocationDescriptions: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public void DeleteLocation(Models.Location objLocation)
        {
            try
            {
                string StoryPath = $"{BasePath}/{objLocation.Story.Title}";
                string LocationsPath = $"{StoryPath}/Locations";
                string LocationPath = $"{LocationsPath}/{objLocation.LocationName}.csv";

                if (objLocation.LocationName.Trim() != "")
                {
                    // Loops through every Chapter and Paragraph and remove the Location
                    var Chapters = GetChapters(objLocation.Story);

                    foreach (var Chapter in Chapters)
                    {
                        var Paragraphs = GetParagraphs(Chapter);

                        foreach (var Paragraph in Paragraphs)
                        {
                            // Create the path to the Paragraph file
                            var ChapterNameParts = Chapter.ChapterName.Split(' ');
                            string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];
                            string ParagraphPath = $"{StoryPath}/Chapters/{ChapterName}/Paragraph{Paragraph.Sequence}.txt";

                            // Get the ParagraphContent from the file
                            string[] ParagraphContent = File.ReadAllLines(ParagraphPath);

                            // Remove all empty lines
                            ParagraphContent = ParagraphContent.Where(line => line.Trim() != "").ToArray();

                            // Get the Location from the file
                            string[] ParagraphLocation = ParagraphContent[0].Split('|');

                            // If the Location is the one to delete, then set it to empty string
                            if (ParagraphLocation[0] == objLocation.LocationName)
                            {
                                ParagraphLocation[0] = "";

                                // Put the ParagraphContent back together
                                ParagraphContent[0] = string.Join("|", ParagraphLocation);

                                // Write the ParagraphContent back to the file
                                File.WriteAllLines(ParagraphPath, ParagraphContent);
                            }
                        }
                    }
                }

                // Delete Location file
                File.Delete(LocationPath);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("DeleteLocation: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public void UpdateLocationName(Models.Location objLocation, string paramOriginalLocationName)
        {
            try
            {
                string StoryPath = $"{BasePath}/{objLocation.Story.Title}";
                string LocationsPath = $"{StoryPath}/Locations";
                string LocationPath = $"{LocationsPath}/{paramOriginalLocationName}.csv";

                if (objLocation.LocationName.Trim() != "")
                {
                    // Loops through every Chapter and Paragraph and remove the Location
                    var Chapters = GetChapters(objLocation.Story);

                    foreach (var Chapter in Chapters)
                    {
                        var Paragraphs = GetParagraphs(Chapter);

                        foreach (var Paragraph in Paragraphs)
                        {
                            // Create the path to the Paragraph file
                            var ChapterNameParts = Chapter.ChapterName.Split(' ');
                            string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];
                            string ParagraphPath = $"{StoryPath}/Chapters/{ChapterName}/Paragraph{Paragraph.Sequence}.txt";

                            // Get the ParagraphContent from the file
                            string[] ParagraphContent = File.ReadAllLines(ParagraphPath);

                            // Remove all empty lines
                            ParagraphContent = ParagraphContent.Where(line => line.Trim() != "").ToArray();

                            // Get the Location from the file
                            string[] ParagraphLocation = ParagraphContent[0].Split('|');

                            // If the Location is the one to update, then set it to new name
                            if (ParagraphLocation[0] == paramOriginalLocationName)
                            {
                                // Set to the new name
                                ParagraphLocation[0] = objLocation.LocationName;

                                // Put the ParagraphContent back together
                                ParagraphContent[0] = string.Join("|", ParagraphLocation);

                                // Write the ParagraphContent back to the file
                                File.WriteAllLines(ParagraphPath, ParagraphContent);
                            }
                        }
                    }

                    // Rename Location file
                    string NewLocationPath = $"{LocationsPath}/{objLocation.LocationName}.csv";
                    File.Move(LocationPath, NewLocationPath);
                }
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("UpdateLocationName: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        #endregion

        #region *** Character ***
        public List<AIStoryBuilders.Models.Character> GetCharacters(Story story)
        {
            // Create a collection of Character
            List<AIStoryBuilders.Models.Character> Characters = new List<AIStoryBuilders.Models.Character>();

            var AIStoryBuildersCharactersPath = $"{BasePath}/{story.Title}/Characters";

            try
            {
                // Get a list of all the Character files
                string[] AIStoryBuildersCharactersFiles = Directory.GetFiles(AIStoryBuildersCharactersPath, "*.csv", SearchOption.AllDirectories);

                // Loop through each Character file
                int i = 1;
                foreach (var AIStoryBuildersCharacterFile in AIStoryBuildersCharactersFiles)
                {
                    // Get the CharacterName from the file name
                    string CharacterName = Path.GetFileNameWithoutExtension(AIStoryBuildersCharacterFile);

                    // Get the CharacterBackgroundContent from the file
                    string[] CharacterBackgroundContent = File.ReadAllLines(AIStoryBuildersCharacterFile);

                    // Remove all empty lines
                    CharacterBackgroundContent = CharacterBackgroundContent.Where(line => line.Trim() != "").ToArray();

                    int ii = 1;
                    List<CharacterBackground> colCharacterBackground = new List<CharacterBackground>();
                    foreach (var CharacterBackground in CharacterBackgroundContent)
                    {
                        // Split CharacterBackground into parts using the pipe character
                        string[] CharacterBackgroundParts = CharacterBackground.Split('|');

                        CharacterBackground objCharacterBackground = new CharacterBackground();

                        objCharacterBackground.Id = ii;
                        objCharacterBackground.Sequence = ii;
                        objCharacterBackground.Type = CharacterBackgroundParts[0];
                        objCharacterBackground.Timeline = new Models.Timeline() { TimelineName = CharacterBackgroundParts[1] };
                        objCharacterBackground.Description = CharacterBackgroundParts[2];
                        objCharacterBackground.VectorContent = CharacterBackgroundParts[3];
                        objCharacterBackground.Character = new Character() { CharacterName = CharacterName };

                        colCharacterBackground.Add(objCharacterBackground);
                        ii++;
                    }

                    // Create a Character
                    AIStoryBuilders.Models.Character Character = new AIStoryBuilders.Models.Character();
                    Character.Id = i;
                    Character.CharacterName = CharacterName;
                    Character.CharacterBackground = colCharacterBackground;

                    // Add Character to collection
                    Characters.Add(Character);
                    i++;
                }

                // Return collection of Characters
                return Characters;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("GetCharacters: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return new List<AIStoryBuilders.Models.Character>();
            }
        }

        public async Task AddUpdateCharacterAsync(Character character, string paramOrginalCharacterName)
        {
            string StoryPath = $"{BasePath}/{character.Story.Title}";
            string CharactersPath = $"{StoryPath}/Characters";
            string ChaptersPath = $"{StoryPath}/Chapters";

            // Add Character to file
            string CharacterName = OrchestratorMethods.SanitizeFileName(paramOrginalCharacterName);

            // Create Character file
            string CharacterPath = $"{CharactersPath}/{CharacterName}.csv";
            List<string> CharacterContents = new List<string>();

            foreach (var description in character.CharacterBackground)
            {
                string description_type = description.Type ?? "";

                string TimeLineName = "";

                if (description.Timeline != null)
                {
                    TimeLineName = description.Timeline.TimelineName ?? "";
                }

                string timeline_name = TimeLineName;
                string VectorDescriptionAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(description.Description ?? "", true);
                CharacterContents.Add($"{description_type}|{timeline_name}|{VectorDescriptionAndEmbedding}" + Environment.NewLine);
            }

            File.WriteAllLines(CharacterPath, CharacterContents);
        }

        public void DeleteCharacter(Character character, string paramOrginalCharcterName)
        {
            string StoryPath = $"{BasePath}/{character.Story.Title}";
            string CharactersPath = $"{StoryPath}/Characters";
            string ChaptersPath = $"{StoryPath}/Chapters";
            string CharacterPath = $"{CharactersPath}/{paramOrginalCharcterName}.csv";

            if (character.CharacterName.Trim() != "")
            {
                // Loops through every Chapter and Paragraph and update the Character
                var Chapters = GetChapters(character.Story);

                foreach (var Chapter in Chapters)
                {
                    var Paragraphs = GetParagraphs(Chapter);

                    foreach (var Paragraph in Paragraphs)
                    {
                        // Create the path to the Paragraph file
                        var ChapterNameParts = Chapter.ChapterName.Split(' ');
                        string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];
                        string ParagraphPath = $"{StoryPath}/Chapters/{ChapterName}/Paragraph{Paragraph.Sequence}.txt";

                        // Get the ParagraphContent from the file
                        string[] ParagraphContent = File.ReadAllLines(ParagraphPath);

                        // Remove all empty lines
                        ParagraphContent = ParagraphContent.Where(line => line.Trim() != "").ToArray();

                        // Get the file as an array
                        string[] ParagraphArray = ParagraphContent[0].Split('|');

                        // Remove the [ and ] from the array
                        ParagraphArray[2] = ParagraphArray[2].Replace("[", "");
                        ParagraphArray[2] = ParagraphArray[2].Replace("]", "");

                        // Get the Character array from the file
                        string[] ParagraphCharacters = ParagraphArray[2].Split(',');

                        // Loop through each Character to see if the Character is the one to delete
                        for (int i = 0; i < ParagraphCharacters.Length; i++)
                        {
                            // If the Character is the one to update, then set it to new name
                            if (ParagraphCharacters[i] == paramOrginalCharcterName)
                            {
                                // Remove the Character
                                ParagraphCharacters[i] = "";
                            }
                        }

                        // Create an array of Characters that are not empty
                        ParagraphCharacters = ParagraphCharacters.Where(x => !string.IsNullOrEmpty(x)).ToArray();

                        // Put the ParagraphCharacters back together
                        string ParagraphCharacterString = string.Join(",", ParagraphCharacters);

                        // Put the [ and ] back on the array
                        ParagraphCharacterString = "[" + ParagraphCharacterString + "]";

                        // Set the Character array back to the ParagraphArray
                        ParagraphArray[2] = ParagraphCharacterString;

                        // Put the ParagraphContent back together
                        ParagraphContent[0] = string.Join("|", ParagraphArray);

                        // Write the ParagraphContent back to the file
                        File.WriteAllLines(ParagraphPath, ParagraphContent);
                    }
                }

                // Delete the Character file
                File.Delete(CharacterPath);
            }
        }

        public void UpdateCharacterName(Character character, string paramOrginalCharcterName)
        {
            string StoryPath = $"{BasePath}/{character.Story.Title}";
            string CharactersPath = $"{StoryPath}/Characters";
            string ChaptersPath = $"{StoryPath}/Chapters";
            string CharacterPath = $"{CharactersPath}/{paramOrginalCharcterName}.csv";

            if (character.CharacterName.Trim() != "")
            {
                // Loops through every Chapter and Paragraph and update the Character
                var Chapters = GetChapters(character.Story);

                foreach (var Chapter in Chapters)
                {
                    var Paragraphs = GetParagraphs(Chapter);

                    foreach (var Paragraph in Paragraphs)
                    {
                        // Create the path to the Paragraph file
                        var ChapterNameParts = Chapter.ChapterName.Split(' ');
                        string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];
                        string ParagraphPath = $"{StoryPath}/Chapters/{ChapterName}/Paragraph{Paragraph.Sequence}.txt";

                        // Get the ParagraphContent from the file
                        string[] ParagraphContent = File.ReadAllLines(ParagraphPath);

                        // Remove all empty lines
                        ParagraphContent = ParagraphContent.Where(line => line.Trim() != "").ToArray();

                        // Get the file as an array
                        string[] ParagraphArray = ParagraphContent[0].Split('|');

                        // Remove the [ and ] from the array
                        ParagraphArray[2] = ParagraphArray[2].Replace("[", "");
                        ParagraphArray[2] = ParagraphArray[2].Replace("]", "");

                        // Get the Character array from the file
                        string[] ParagraphCharacters = ParagraphArray[2].Split(',');

                        // Loop through each Character to see if the Character is the one to update
                        for (int i = 0; i < ParagraphCharacters.Length; i++)
                        {
                            // If the Character is the one to update, then set it to new name
                            if (ParagraphCharacters[i] == paramOrginalCharcterName)
                            {
                                // Set to the new name
                                ParagraphCharacters[i] = character.CharacterName;
                            }
                        }

                        // Put the ParagraphCharacters back together
                        string ParagraphCharacterString = string.Join(",", ParagraphCharacters);

                        // Put the [ and ] back on the array
                        ParagraphCharacterString = "[" + ParagraphCharacterString + "]";

                        // Set the Character array back to the ParagraphArray
                        ParagraphArray[2] = ParagraphCharacterString;

                        // Put the ParagraphContent back together
                        ParagraphContent[0] = string.Join("|", ParagraphArray);

                        // Write the ParagraphContent back to the file
                        File.WriteAllLines(ParagraphPath, ParagraphContent);
                    }
                }

                // Rename Character file
                string NewCharacterPath = $"{CharactersPath}/{character.CharacterName.Trim()}.csv";
                File.Move(CharacterPath, NewCharacterPath);
            }
        }
        #endregion

        #region *** Chapter ***
        public List<AIStoryBuilders.Models.Chapter> GetChapters(Story story)
        {
            // Create a collection of Chapter
            List<AIStoryBuilders.Models.Chapter> Chapters = new List<AIStoryBuilders.Models.Chapter>();

            var AIStoryBuildersChaptersPath = $"{BasePath}/{story.Title}/Chapters";

            try
            {
                // Get a list of all the Chapter folders
                // order by the folder name

                string[] AIStoryBuildersChaptersFolders = Directory.GetDirectories(AIStoryBuildersChaptersPath);

                // order by the folder name
                AIStoryBuildersChaptersFolders = AIStoryBuildersChaptersFolders.OrderBy(x => x).ToArray();

                // Loop through each Chapter folder
                foreach (var AIStoryBuildersChapterFolder in AIStoryBuildersChaptersFolders)
                {
                    // Get the ChapterName from the file name                    
                    string ChapterName = Path.GetFileNameWithoutExtension(AIStoryBuildersChapterFolder);
                    string ChapterFileName = Path.Combine(AIStoryBuildersChapterFolder, $"{ChapterName}.txt");

                    // Put in a space after the word Chapter
                    ChapterName = ChapterName.Insert(7, " ");

                    // Get sequence number from folder name
                    string ChapterSequence = ChapterName.Split(' ')[1];
                    int ChapterSequenceNumber = int.Parse(ChapterSequence);

                    // Get the ChapterContent from the file
                    string[] ChapterContent = File.ReadAllLines(ChapterFileName);

                    // Remove all empty lines
                    ChapterContent = ChapterContent.Where(line => line.Trim() != "").ToArray();

                    var ChapterDescription = ChapterContent.Select(x => x.Split('|')).Select(x => x[0]).FirstOrDefault();

                    // Create a Chapter
                    AIStoryBuilders.Models.Chapter Chapter = new AIStoryBuilders.Models.Chapter();
                    Chapter.ChapterName = ChapterName;
                    Chapter.Sequence = ChapterSequenceNumber;
                    Chapter.Synopsis = ChapterDescription;
                    Chapter.Story = story;

                    // Add Chapter to collection
                    Chapters.Add(Chapter);
                }

                // Return collection of Chapters
                return Chapters.OrderBy(x => x.Sequence).ToList();
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("GetChapters: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return new List<AIStoryBuilders.Models.Chapter>();
            }
        }

        public int CountChapters(Story story)
        {
            int ChapterCount = 0;

            try
            {
                var AIStoryBuildersChaptersPath = $"{BasePath}/{story.Title}/Chapters";
                string[] AIStoryBuildersChaptersFolders = Directory.GetDirectories(AIStoryBuildersChaptersPath);

                ChapterCount = AIStoryBuildersChaptersFolders.Count();

                return ChapterCount;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("CountChapters: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return 0;
            }
        }

        public async Task AddChapterAsync(Chapter objChapter, string ChapterName)
        {
            if (objChapter.Synopsis == null)
            {
                objChapter.Synopsis = " ";
            }

            var AIStoryBuildersChaptersPath = $"{BasePath}/{objChapter.Story.Title}/Chapters";

            // Create the Chapter folder
            string ChapterPath = $"{AIStoryBuildersChaptersPath}/{ChapterName}";
            Directory.CreateDirectory(ChapterPath);

            // Create the Chapter file
            string ChapterFilePath = $"{ChapterPath}/{ChapterName}.txt";
            string ChapterSynopsisAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(objChapter.Synopsis, true);
            File.WriteAllText(ChapterFilePath, $"{ChapterSynopsisAndEmbedding}");
        }

        public async Task InsertChapterAsync(Chapter objChapter)
        {
            if (objChapter.Synopsis == null)
            {
                objChapter.Synopsis = " ";
            }

            string ChapterName = objChapter.ChapterName.Replace(" ", "");
            var AIStoryBuildersChaptersPath = $"{BasePath}/{objChapter.Story.Title}/Chapters";

            // Create the Chapter folder
            string ChapterPath = $"{AIStoryBuildersChaptersPath}/{ChapterName}";
            Directory.CreateDirectory(ChapterPath);

            // Create the Chapter file
            string ChapterFilePath = $"{ChapterPath}/{ChapterName}.txt";
            string ChapterSynopsisAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(objChapter.Synopsis, true);
            File.WriteAllText(ChapterFilePath, $"{ChapterSynopsisAndEmbedding}");
        }

        public async Task UpdateChapterAsync(Chapter objChapter)
        {
            string ChapterName = objChapter.ChapterName.Replace(" ", "");
            var AIStoryBuildersChaptersPath = $"{BasePath}/{objChapter.Story.Title}/Chapters";
            string ChapterPath = $"{AIStoryBuildersChaptersPath}/{ChapterName}";
            string ChapterFilePath = $"{ChapterPath}/{ChapterName}.txt";

            string ChapterSynopsisAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(objChapter.Synopsis, true);
            File.WriteAllText(ChapterFilePath, $"{ChapterSynopsisAndEmbedding}");
        }

        public void DeleteChapter(Chapter objChapter)
        {
            // Delete Chapter
            string ChapterName = objChapter.ChapterName.Replace(" ", "");
            var AIStoryBuildersChaptersPath = $"{BasePath}/{objChapter.Story.Title}/Chapters";
            string ChapterPath = $"{AIStoryBuildersChaptersPath}/{ChapterName}";

            // Delete folder
            Directory.Delete(ChapterPath, true);
        }
        #endregion

        #region *** Paragraph ***
        public List<AIStoryBuilders.Models.Paragraph> GetParagraphs(Chapter chapter)
        {
            List<Paragraph> colParagraphs = new List<Paragraph>();

            try
            {
                var ChapterNameParts = chapter.ChapterName.Split(' ');
                string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];

                var AIStoryBuildersParagraphsPath = $"{BasePath}/{chapter.Story.Title}/Chapters/{ChapterName}";

                // Get a list of all the Paragraph files
                string[] AIStoryBuildersParagraphsFiles = Directory.GetFiles(AIStoryBuildersParagraphsPath, "Paragraph*.txt", SearchOption.AllDirectories);

                // Loop through each Paragraph file
                foreach (var AIStoryBuildersParagraphFile in AIStoryBuildersParagraphsFiles)
                {
                    // Get the ParagraphName from the file name
                    string ParagraphName = Path.GetFileNameWithoutExtension(AIStoryBuildersParagraphFile);

                    // Put in a space after the word ParagraphName
                    ParagraphName = ParagraphName.Insert(9, " ");

                    // Get sequence number from Paragraph Name
                    string ParagraphSequence = ParagraphName.Split(' ')[1];
                    int ParagraphSequenceNumber = int.Parse(ParagraphSequence);

                    // Get the ChapterContent from the file
                    string[] ChapterContent = File.ReadAllLines(AIStoryBuildersParagraphFile);

                    // Remove all empty lines
                    ChapterContent = ChapterContent.Where(line => line.Trim() != "").ToArray();

                    // Concatonate all lines into one string
                    string RawParagraphContent = string.Join("\n", ChapterContent);

                    // Spilit the string into parts using the pipe character
                    string[] RawParagraphContentParts = RawParagraphContent.Split('|');

                    var ParagraphLocation = RawParagraphContentParts[0];
                    var ParagraphTimeline = RawParagraphContentParts[1];
                    var ParagraphCharactersRaw = RawParagraphContentParts[2];
                    var ParagraphContent = RawParagraphContentParts[3];

                    // Convert ParagraphCharactersRaw to a List
                    List<string> ParagraphCharacters = ParseStringToList(ParagraphCharactersRaw);

                    // Convert to List<Models.Character>
                    List<Models.Character> Characters = new List<Models.Character>();
                    foreach (var ParagraphCharacter in ParagraphCharacters)
                    {
                        Characters.Add(new Models.Character() { CharacterName = ParagraphCharacter });
                    }

                    // Create a Paragraph
                    AIStoryBuilders.Models.Paragraph Paragraph = new AIStoryBuilders.Models.Paragraph();
                    Paragraph.Sequence = ParagraphSequenceNumber;
                    Paragraph.Location = new Models.Location() { LocationName = ParagraphLocation };
                    Paragraph.Timeline = new Models.Timeline() { TimelineName = ParagraphTimeline };
                    Paragraph.Characters = Characters;
                    Paragraph.ParagraphContent = ParagraphContent;

                    // Add Paragraph to collection
                    colParagraphs.Add(Paragraph);
                }

                return colParagraphs.OrderBy(x => x.Sequence).ToList();
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("GetParagraphs: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return new List<AIStoryBuilders.Models.Paragraph>();
            }
        }

        public List<AIParagraph> GetParagraphVectors(Chapter chapter, string TimelineName)
        {
            List<AIParagraph> colParagraphs = new List<AIParagraph>();

            try
            {
                var ChapterNameParts = chapter.ChapterName.Split(' ');
                string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];

                var AIStoryBuildersParagraphsPath = $"{BasePath}/{chapter.Story.Title}/Chapters/{ChapterName}";

                // Get a list of all the Paragraph files
                string[] AIStoryBuildersParagraphsFiles = Directory.GetFiles(AIStoryBuildersParagraphsPath, "Paragraph*.txt", SearchOption.AllDirectories);

                // Loop through each Paragraph file
                foreach (var AIStoryBuildersParagraphFile in AIStoryBuildersParagraphsFiles)
                {
                    // Get the ParagraphName from the file name
                    string ParagraphName = Path.GetFileNameWithoutExtension(AIStoryBuildersParagraphFile);

                    // Put in a space after the word ParagraphName
                    ParagraphName = ParagraphName.Insert(9, " ");

                    // Get sequence number from Paragraph Name
                    string ParagraphSequence = ParagraphName.Split(' ')[1];
                    int ParagraphSequenceNumber = int.Parse(ParagraphSequence);

                    // Get the ChapterContent from the file
                    string[] ChapterContent = File.ReadAllLines(AIStoryBuildersParagraphFile);

                    // Remove all empty lines
                    ChapterContent = ChapterContent.Where(line => line.Trim() != "").ToArray();

                    var ParagraphLocation = ChapterContent.Select(x => x.Split('|')).Select(x => x[0]).FirstOrDefault();
                    var ParagraphTimeline = ChapterContent.Select(x => x.Split('|')).Select(x => x[1]).FirstOrDefault();
                    var ParagraphCharactersRaw = ChapterContent.Select(x => x.Split('|')).Select(x => x[2]).FirstOrDefault();
                    var ParagraphContent = ChapterContent.Select(x => x.Split('|')).Select(x => x[3]).FirstOrDefault();
                    var ParagraphVectors = ChapterContent.Select(x => x.Split('|')).Select(x => x[4]).FirstOrDefault();

                    // Only get Paragraphs for the specified Timeline
                    if (TimelineName == ParagraphTimeline)
                    {
                        // Convert ParagraphCharactersRaw to a List
                        List<string> ParagraphCharacters = ParseStringToList(ParagraphCharactersRaw);

                        // Convert to List<Models.Character>
                        string[] CharactersArray = new string[ParagraphCharacters.Count()];
                        int i = 0;
                        foreach (var ParagraphCharacter in ParagraphCharacters)
                        {
                            CharactersArray[i] = ParagraphCharacter;
                            i++;
                        }

                        // Create a Paragraph
                        AIParagraph Paragraph = new AIParagraph();
                        Paragraph.sequence = ParagraphSequenceNumber;
                        Paragraph.location_name = ParagraphLocation;
                        Paragraph.timeline_name = ParagraphTimeline;
                        Paragraph.character_names = CharactersArray;
                        Paragraph.contents = ParagraphContent;
                        Paragraph.vectors = ParagraphVectors;

                        // Add Paragraph to collection
                        colParagraphs.Add(Paragraph);
                    }
                }

                return colParagraphs.OrderBy(x => x.sequence).ToList();
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("GetParagraphVectors: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return new List<AIParagraph>();
            }
        }

        public int CountParagraphs(Chapter chapter)
        {
            int ParagraphCount = 0;

            try
            {
                var ChapterNameParts = chapter.ChapterName.Split(' ');
                string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];

                var AIStoryBuildersParagraphsPath = $"{BasePath}/{chapter.Story.Title}/Chapters/{ChapterName}";

                // Get a list of all the Paragraph files
                string[] AIStoryBuildersParagraphsFiles = Directory.GetFiles(AIStoryBuildersParagraphsPath, "Paragraph*.txt", SearchOption.AllDirectories);

                ParagraphCount = AIStoryBuildersParagraphsFiles.Count();

                return ParagraphCount;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("CountParagraphs: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                // File is empty
                return 0;
            }
        }

        public void AddParagraph(Chapter chapter, Paragraph Paragraph)
        {
            try
            {
                // First restructure the existing Paragraphs
                RestructureParagraphs(chapter, Paragraph.Sequence, RestructureType.Add);

                // Create a file for the new Paragraph
                var ChapterNameParts = chapter.ChapterName.Split(' ');
                string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];

                var AIStoryBuildersParagraphsPath = $"{BasePath}/{chapter.Story.Title}/Chapters/{ChapterName}";

                // Create the Paragraph file
                string ParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{Paragraph.Sequence}.txt";

                // Create the ParagraphContent
                string VectorDescriptionAndEmbedding = "|";
                string ParagraphContent = $"{Paragraph.Location.LocationName ?? ""}|{Paragraph.Timeline.TimelineName ?? ""}|[{string.Join(",", Paragraph.Characters.Select(x => x.CharacterName))}]|{VectorDescriptionAndEmbedding}";

                // Write the ParagraphContent to the file
                File.WriteAllText(ParagraphPath, ParagraphContent);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("UpdateParagraph: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public async Task UpdateParagraph(Chapter chapter, Paragraph Paragraph)
        {
            try
            {
                var ChapterNameParts = chapter.ChapterName.Split(' ');
                string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];

                var AIStoryBuildersParagraphsPath = $"{BasePath}/{chapter.Story.Title}/Chapters/{ChapterName}";

                // Create the Paragraph file
                string ParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{Paragraph.Sequence}.txt";

                // Create the ParagraphContent
                string VectorDescriptionAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(Paragraph.ParagraphContent, true);
                string ParagraphContent = $"{Paragraph.Location.LocationName ?? ""}|{Paragraph.Timeline.TimelineName ?? ""}|[{string.Join(",", Paragraph.Characters.Select(x => x.CharacterName))}]|{VectorDescriptionAndEmbedding}";

                // Preserve any line breaks
                ParagraphContent = ParagraphContent.Replace("\n", "\r\n");

                // Write the ParagraphContent to the file
                File.WriteAllText(ParagraphPath, ParagraphContent);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("UpdateParagraph: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public void DeleteParagraph(Chapter chapter, Paragraph Paragraph)
        {
            try
            {
                var ChapterNameParts = chapter.ChapterName.Split(' ');
                string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];

                var AIStoryBuildersParagraphsPath = $"{BasePath}/{chapter.Story.Title}/Chapters/{ChapterName}";

                // Delete the Paragraph file
                string ParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{Paragraph.Sequence}.txt";
                File.Delete(ParagraphPath);

                RestructureParagraphs(chapter, Paragraph.Sequence, RestructureType.Delete);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("DeleteParagraph: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }

        public List<Paragraph> AddParagraphIndenting(List<Paragraph> paramParagraphs)
        {
            List<Paragraph> colParagraphs = new List<Paragraph>();

            foreach (var Paragraph in paramParagraphs)
            {
                Paragraph.ParagraphContent = "&nbsp;&nbsp;&nbsp;&nbsp;" + Paragraph.ParagraphContent;
                Paragraph.ParagraphContent = Paragraph.ParagraphContent.Replace("\n", "<br />&nbsp;&nbsp;&nbsp;&nbsp;");
                colParagraphs.Add(Paragraph);
            }

            return colParagraphs;
        }

        public Paragraph AddParagraphIndenting(Paragraph paramParagraph)
        {
            Paragraph objParagraph = new Paragraph();

            objParagraph.ParagraphContent = "&nbsp;&nbsp;&nbsp;&nbsp;" + paramParagraph.ParagraphContent;
            objParagraph.ParagraphContent = objParagraph.ParagraphContent.Replace("\n", "<br />&nbsp;&nbsp;&nbsp;&nbsp;");
            objParagraph.Chapter = paramParagraph.Chapter;
            objParagraph.Characters = paramParagraph.Characters;
            objParagraph.Location = paramParagraph.Location;
            objParagraph.Sequence = paramParagraph.Sequence;
            objParagraph.Timeline = paramParagraph.Timeline;
            objParagraph.Id = paramParagraph.Id;

            return objParagraph;
        }

        public List<Paragraph> RemoveParagraphIndenting(List<Paragraph> paramParagraphs)
        {
            List<Paragraph> colParagraphs = new List<Paragraph>();

            foreach (var Paragraph in paramParagraphs)
            {
                Paragraph.ParagraphContent = Paragraph.ParagraphContent.Replace("&nbsp;", "");
                Paragraph.ParagraphContent = Paragraph.ParagraphContent.Replace("<br />", "\n");
                colParagraphs.Add(Paragraph);
            }

            return colParagraphs;
        }

        public Paragraph RemoveParagraphIndenting(Paragraph paramParagraph)
        {
            Paragraph objParagraph = new Paragraph();

            objParagraph.ParagraphContent = paramParagraph.ParagraphContent.Replace("&nbsp;", "");
            objParagraph.ParagraphContent = objParagraph.ParagraphContent.Replace("<br />", "\n");
            objParagraph.Chapter = paramParagraph.Chapter;
            objParagraph.Characters = paramParagraph.Characters;
            objParagraph.Location = paramParagraph.Location;
            objParagraph.Sequence = paramParagraph.Sequence;
            objParagraph.Timeline = paramParagraph.Timeline;
            objParagraph.Id = paramParagraph.Id;

            return objParagraph;
        }
        #endregion
    }
}
