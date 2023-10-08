using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Newtonsoft.Json;
using OpenAI.Files;

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
            string TimelinesPath = $"{StoryPath}/Timelines";
            string LocationsPath = $"{StoryPath}/Locations";

            CreateDirectory(StoryPath);
            CreateDirectory(CharactersPath);
            CreateDirectory(ChaptersPath);
            CreateDirectory(TimelinesPath);
            CreateDirectory(LocationsPath);

            var AIStoryBuildersCharactersPath = $"{CharactersPath}/Characters.csv";
            var AIStoryBuildersChaptersPath = $"{ChaptersPath}/Chapters.csv";
            var AIStoryBuildersTimelinesPath = $"{TimelinesPath}/Timelines.csv";
            var AIStoryBuildersLocationsPath = $"{LocationsPath}/Locations.csv";

            CreateFile(AIStoryBuildersCharactersPath, "");
            CreateFile(AIStoryBuildersChaptersPath, "");
            CreateFile(AIStoryBuildersTimelinesPath, "");
            CreateFile(AIStoryBuildersLocationsPath, "");

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
            var ParsedStoryObjectDynamic = JsonConvert.DeserializeObject<dynamic>(ParsedStory);

        }

        public void UpdateStory(Story story)
        {
            // Get Story
            var AIStoryBuildersStoriesPath = $"{BasePath}/AIStoryBuildersStories.csv";
            string[] AIStoryBuildersStoriesContent = ReadCSVFile(AIStoryBuildersStoriesPath);

            // Remove all empty lines
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Trim() != "").ToArray();

            // Get all lines except the one to update
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Split('|')[1] != story.Title).ToArray();

            // Trim all lines
            AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Select(line => line.Trim()).ToArray();

            // Add Story to file
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
        //public async Task<List<Character>> GetCharactersAsync(Story story)
        //{
        //    // Get Characters including CharacterCharacterBackground
        //    return await _context.Character
        //        .Include(character => character.CharacterBackground)
        //        .OrderBy(character => character.CharacterName)
        //        .Where(character => character.StoryId == story.Id)
        //        .AsNoTracking().ToListAsync();
        //}

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

    }
}
