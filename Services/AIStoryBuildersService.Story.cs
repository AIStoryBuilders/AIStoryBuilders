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

            // Parse the Story to create the files
            var ParsedStory = await OrchestratorMethods.ParseNewStory(story.Title, story.Synopsis);

            // Convert the JSON to a dynamic object
            JSONNewStory ParsedNewStory = JsonConvert.DeserializeObject<JSONNewStory>(ParsedStory);

            // Create the Character files
            TextEvent?.Invoke(this, new TextEventArgs($"Create the Character files"));
            foreach (var character in ParsedNewStory.characters)
            {
                // Add Character to file
                string CharacterName = OrchestratorMethods.SanitizeFileName(character.name);

                // Create Character file
                string CharacterPath = $"{CharactersPath}/{CharacterName}.csv";
                List<string> CharacterContents = new List<string>();

                foreach (var description in character.descriptions)
                {
                    string VectorDescriptionAndEmbedding = await OrchestratorMethods.GetVectorEmbedding(description);
                    CharacterContents.Add($"{VectorDescriptionAndEmbedding}" + Environment.NewLine);
                }

                File.WriteAllLines(CharacterPath, CharacterContents);
            }

            // Create the Location files
            TextEvent?.Invoke(this, new TextEventArgs($"Create the Location files"));
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
            TextEvent?.Invoke(this, new TextEventArgs($"Create the Timeline file"));
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

            // **** Create the First Paragraph in the first Chapter

            // Create a folder at: Chapters/Chapter1            
            string ChapterPath = $"{ChaptersPath}/Chapter1";
            CreateDirectory(ChapterPath);

            // Create a file at: Chapters/Chapter1/Chapter.txt
            string ChapterFilePath = $"{ChapterPath}/Chapter.txt";
            File.WriteAllText(ChapterFilePath, $"Chapter One|");

            // Create a file at: Chapters/Chapter1/Paragraph1.txt
            TextEvent?.Invoke(this, new TextEventArgs($"Create Chapters/Chapter1/Paragraph1.txt"));
            string FirstParagraphPath = $"{ChapterPath}/Paragraph1.txt";
            string VectorDescriptionAndEmbeddingFirstParagraph = await OrchestratorMethods.GetVectorEmbedding(ParsedNewStory.firstparagraph);
            File.WriteAllText(FirstParagraphPath, $"||{VectorDescriptionAndEmbeddingFirstParagraph}");
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
