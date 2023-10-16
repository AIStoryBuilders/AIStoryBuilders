using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Newtonsoft.Json;
using OpenAI.Files;
using static AIStoryBuilders.Model.OrchestratorMethods;

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
                LogService.WriteToLog(ex.Message);

                // File is empty
                return new List<Story>();
            }
        }
        public async Task AddStory(Story story)
        {
            // Create Characters, Chapters, Timelines, and Locations sub folders

            string StoryPath = $"{BasePath}/{story.Title}";
            string CharactersPath = $"{StoryPath}/Characters";
            string ChaptersPath = $"{StoryPath}/Chapters";
            string LocationsPath = $"{StoryPath}/Locations";

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

            //  ********** Call the LLM to Parse the Story to create the files **********
            var ParsedStoryJSON = await OrchestratorMethods.ParseNewStory(story.Title, story.Synopsis);

            JSONStory ParsedNewStory = new JSONStory();

            // Convert the JSON to a dynamic object
            ParsedNewStory = ParseJSONNewStory(GetOnlyJSON(ParsedStoryJSON));

            // Test to see that something was returned
            if(ParsedNewStory.characters.Length == 0)
            {
                // Clean the JSON
                ParsedStoryJSON = await OrchestratorMethods.CleanJSON(GetOnlyJSON(ParsedStoryJSON));

                // Convert the JSON to a dynamic object
                ParsedNewStory = ParseJSONNewStory(GetOnlyJSON(ParsedStoryJSON));
            }

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
                    string VectorDescriptionAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(description.description ?? "");
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

                foreach (var description in location.descriptions)
                {
                    string VectorDescriptionAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(description);
                    LocationContents.Add($"{VectorDescriptionAndEmbedding}" + Environment.NewLine);
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

            // Create Timeline file
            string TimelinePath = $"{StoryPath}/Timelines.csv";
            File.WriteAllLines(TimelinePath, TimelineContents);

            //// **** Create the First Paragraph and the Chapters
            
            // Call ChatGPT
            var ParsedChaptersJSON = await OrchestratorMethods.CreateNewChapters(ParsedStoryJSON, story.ChapterCount);

            JSONChapters ParsedNewChapters = new JSONChapters();

            // Convert the JSON to a dynamic object
            ParsedNewChapters = ParseJSONNewChapters(GetOnlyJSON(ParsedChaptersJSON));

            // Test to see that something was returned
            if (ParsedNewChapters.chapter.Length == 0)
            {
                // Clean the JSON
                ParsedChaptersJSON = await OrchestratorMethods.CleanJSON(GetOnlyJSON(ParsedChaptersJSON));

                // Convert the JSON to a dynamic object
                ParsedNewChapters = ParseJSONNewChapters(GetOnlyJSON(ParsedChaptersJSON));
            }

            //// **** Create the Files

            int ChapterNumber = 1;
            foreach (var chapter in ParsedNewChapters.chapter)
            {
                // Create a folder in Chapters/
                string ChapterPath = $"{ChaptersPath}/{chapter.chapter_name}";
                CreateDirectory(ChapterPath);

                TextEvent?.Invoke(this, new TextEventArgs($"Create Chapter {ChapterNumber}", 5));

                if (chapter.chapter_synopsis != null)
                {
                    // Create a file at: Chapters/Chapter{ChapterNumber}/Chapter{ChapterNumber}.txt
                    string ChapterFilePath = $"{ChapterPath}/Chapter{ChapterNumber}.txt";
                    string ChapterSynopsisAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(chapter.chapter_synopsis);
                    File.WriteAllText(ChapterFilePath, $"{ChapterSynopsisAndEmbedding}");

                    if (chapter.paragraphs[0] != null)
                    {
                        // Create a file at: Chapters/Chapter1/Paragraph1.txt
                        string FirstParagraphPath = $"{ChapterPath}/Paragraph1.txt";
                        string VectorDescriptionAndEmbeddingFirstParagraph = await OrchestratorMethods.GetVectorEmbedding(chapter.paragraphs[0].contents);

                        string Location = chapter.paragraphs[0].location_name;
                        string Timeline = chapter.paragraphs[0].timeline_name;
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
                    Timeline.StoryId = story.Id;
                    Timeline.TimelineName = TimelineName;
                    Timeline.TimelineDescription = TimelineDescription;
                    Timeline.StartDate = DateTime.Parse(TimelineStartTime);
                    Timeline.StopDate = DateTime.Parse(TimelineStopTime);

                    // Add Timeline to collection
                    Timelines.Add(Timeline);
                }

                // Return collection of Timelines
                return Timelines;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog(ex.Message);

                // File is empty
                return new List<AIStoryBuilders.Models.Timeline>();
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
                foreach (var AIStoryBuildersLocationFile in AIStoryBuildersLocationsFiles)
                {
                    // Get the LocationName from the file name
                    string LocationName = Path.GetFileNameWithoutExtension(AIStoryBuildersLocationFile);

                    // Get the LocationContent from the file
                    string[] LocationContent = File.ReadAllLines(AIStoryBuildersLocationFile);

                    // Remove all empty lines
                    LocationContent = LocationContent.Where(line => line.Trim() != "").ToArray();

                    var LocationDescription = LocationContent.Select(x => x.Split('|')).Select(x => x[0]).FirstOrDefault();

                    // LocationBackgrounds

                    // Create a Location
                    AIStoryBuilders.Models.Location Location = new AIStoryBuilders.Models.Location();
                    Location.StoryId = story.Id;
                    Location.LocationName = LocationName;
                    Location.Description = LocationDescription;

                    // Add Location to collection
                    Locations.Add(Location);
                }

                // Return collection of Locations
                return Locations;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog(ex.Message);

                // File is empty
                return new List<AIStoryBuilders.Models.Location>();
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
                foreach (var AIStoryBuildersCharacterFile in AIStoryBuildersCharactersFiles)
                {
                    // Get the CharacterName from the file name
                    string CharacterName = Path.GetFileNameWithoutExtension(AIStoryBuildersCharacterFile);

                    // Get the CharacterBackgroundContent from the file
                    string[] CharacterBackgroundContent = File.ReadAllLines(AIStoryBuildersCharacterFile);

                    // Remove all empty lines
                    CharacterBackgroundContent = CharacterBackgroundContent.Where(line => line.Trim() != "").ToArray();

                    var CharacterBackgrounds = CharacterBackgroundContent.Select(x => x.Split('|')).Select(x => x[0]).ToList();

                    // CharacterBackgrounds

                    // Create a Character
                    AIStoryBuilders.Models.Character Character = new AIStoryBuilders.Models.Character();
                    Character.StoryId = story.Id;
                    Character.CharacterName = CharacterName;
                    Character.CharacterBackground = new List<CharacterBackground>();

                    foreach (var CharacterBackground in CharacterBackgrounds)
                    {
                        Character.CharacterBackground.Add(new CharacterBackground { Description = CharacterBackground });
                    }

                    // Add Character to collection
                    Characters.Add(Character);
                }

                // Return collection of Characters
                return Characters;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog(ex.Message);

                // File is empty
                return new List<AIStoryBuilders.Models.Character>();
            }
        }

        //public async Task<Character> GetCharacterAsync(int id)
        //{
        //    // Get Character
        //    return await _context.Character.FindAsync(id);
        //}

        //public async Task<Character> AddCharacterAsync(Character character)
        //{
        //    // Ensure no duplicate CharacterName
        //    var duplicateCharacter = await _context.Character
        //        .AsNoTracking()
        //        .Where(c => c.StoryId == character.StoryId)
        //        .Where(c => c.CharacterName == character.CharacterName)
        //        .FirstOrDefaultAsync();

        //    if (duplicateCharacter != null)
        //    {
        //        // Throw exception
        //        throw new Exception("Duplicate CharacterName");
        //    }

        //    // Add Character

        //    Character newCharacter = new Character();
        //    newCharacter.StoryId = character.StoryId;
        //    newCharacter.CharacterName = character.CharacterName ?? "";
        //    newCharacter.Description = character.Description ?? "";
        //    newCharacter.Goals = character.Goals ?? "";

        //    _context.Character.Add(newCharacter);
        //    await _context.SaveChangesAsync();
        //    return newCharacter;
        //}

        //public async Task<Character> UpdateCharacterAsync(Character character)
        //{
        //    // Get Character
        //    var characterToUpdate = await _context.Character.FindAsync(character.Id);

        //    // Update each value
        //    characterToUpdate.CharacterName = character.CharacterName ?? "";
        //    characterToUpdate.Description = character.Description ?? "";
        //    characterToUpdate.Goals = character.Goals ?? "";

        //    _context.Character.Update(characterToUpdate);
        //    await _context.SaveChangesAsync();

        //    return characterToUpdate;
        //}

        //public async Task<Character> DeleteCharacterAsync(int id)
        //{
        //    // Delete Character
        //    var character = await _context.Character.FindAsync(id);
        //    _context.Character.Remove(character);
        //    await _context.SaveChangesAsync();
        //    return character;
        //}
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
                    Chapter.StoryId = story.Id;
                    Chapter.ChapterName = ChapterName;
                    Chapter.Sequence = ChapterSequenceNumber;
                    Chapter.Synopsis = ChapterDescription;

                    // Add Chapter to collection
                    Chapters.Add(Chapter);
                }

                // Return collection of Chapters
                return Chapters.OrderBy(x => x.Sequence).ToList();
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog(ex.Message);

                // File is empty
                return new List<AIStoryBuilders.Models.Chapter>();
            }
        }

        //public async Task<Chapter> GetChapterAsync(int id)
        //{
        //    // Get Chapter
        //    return await _context.Chapter.FindAsync(id);
        //}

        //public async Task<Chapter> AddChapterAsync(Chapter chapter)
        //{
        //    // Add Chapter
        //    _context.Chapter.Add(chapter);
        //    await _context.SaveChangesAsync();
        //    return chapter;
        //}

        //public async Task<Chapter> UpdateChapterAsync(Chapter chapter)
        //{
        //    // Update Chapter
        //    _context.Entry(chapter).State = EntityState.Modified;
        //    await _context.SaveChangesAsync();
        //    return chapter;
        //}

        //public async Task<Chapter> DeleteChapterAsync(int id)
        //{
        //    // Delete Chapter
        //    var chapter = await _context.Chapter.FindAsync(id);
        //    _context.Chapter.Remove(chapter);
        //    await _context.SaveChangesAsync();
        //    return chapter;
        //}
        #endregion
    }
}
