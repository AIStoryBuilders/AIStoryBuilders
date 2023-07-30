using AIStoryBuilders.Models;
using Microsoft.EntityFrameworkCore;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region *** Story ***
        public async Task<List<Story>> GetStorysAsync()
        {
            // Get Storys including Chapters           
            return await _context.Story
                .Include(story => story.Chapter)
                .OrderBy(story => story.Title)
                .AsNoTracking().ToListAsync();
        }

        public async Task<Story> GetStoryAsync(int id)
        {
            // Get Story including Chapters
            return await _context.Story
                .Include(story => story.Chapter)
                .AsNoTracking()
                .FirstOrDefaultAsync(story => story.Id == id);
        }

        public async Task<Story> AddStoryAsync(Story story)
        {
            // Add Story

            Story newStory = new Story
            {
                Title = story.Title ?? "",
                Style = story.Style ?? "",
                Theme = story.Theme ?? "",
                Synopsis = story.Synopsis ?? "",
            };

            _context.Story.Add(newStory);
            await _context.SaveChangesAsync();
            return newStory;
        }

        public async Task<Story> UpdateStoryAsync(Story story)
        {
            // Get Story
            var storyToUpdate = await _context.Story.FindAsync(story.Id);

            // Update each value
            storyToUpdate.Title = story.Title ?? "";
            storyToUpdate.Style = story.Style ?? "";
            storyToUpdate.Theme = story.Theme ?? "";
            storyToUpdate.Synopsis = story.Synopsis ?? "";

            // Update Story
            await _context.SaveChangesAsync();
            return storyToUpdate;
        }

        public async Task<Story> DeleteStoryAsync(int id)
        {
            // Delete Story
            var story = await _context.Story.FindAsync(id);
            _context.Story.Remove(story);
            await _context.SaveChangesAsync();
            return story;
        }
        #endregion

        #region *** Character ***
        public async Task<List<Character>> GetCharactersAsync(Story story)
        {
            // Get Characters including CharacterCharacterBackground
            return await _context.Character
                .Include(character => character.CharacterBackground)
                .OrderBy(character => character.CharacterName)
                .Where(character => character.StoryId == story.Id)
                .AsNoTracking().ToListAsync();
        }

        public async Task<Character> GetCharacterAsync(int id)
        {
            // Get Character
            return await _context.Character.FindAsync(id);
        }

        public async Task<Character> AddCharacterAsync(Character character)
        {
            // Ensure no duplicate CharacterName
            var duplicateCharacter = await _context.Character
                .AsNoTracking()
                .Where(c => c.StoryId == character.StoryId)
                .Where(c => c.CharacterName == character.CharacterName)
                .FirstOrDefaultAsync();

            if (duplicateCharacter != null)
            {
                // Throw exception
                throw new Exception("Duplicate CharacterName");
            }

            // Add Character

            Character newCharacter = new Character();
            newCharacter.StoryId = character.StoryId;
            newCharacter.CharacterName = character.CharacterName ?? "";
            newCharacter.Description = character.Description ?? "";
            newCharacter.Goals = character.Goals ?? "";

            _context.Character.Add(newCharacter);
            await _context.SaveChangesAsync();
            return newCharacter;
        }

        public async Task<Character> UpdateCharacterAsync(Character character)
        {
            // Get Character
            var characterToUpdate = await _context.Character.FindAsync(character.Id);

            // Update each value
            characterToUpdate.CharacterName = character.CharacterName ?? "";
            characterToUpdate.Description = character.Description ?? "";
            characterToUpdate.Goals = character.Goals ?? "";

            _context.Character.Update(characterToUpdate);
            await _context.SaveChangesAsync();

            return characterToUpdate;
        }

        public async Task<Character> DeleteCharacterAsync(int id)
        {
            // Delete Character
            var character = await _context.Character.FindAsync(id);
            _context.Character.Remove(character);
            await _context.SaveChangesAsync();
            return character;
        }
        #endregion

        #region *** Chapter ***
        public async Task<Chapter> GetChapterAsync(int id)
        {
            // Get Chapter
            return await _context.Chapter.FindAsync(id);
        }

        public async Task<Chapter> AddChapterAsync(Chapter chapter)
        {
            // Add Chapter
            _context.Chapter.Add(chapter);
            await _context.SaveChangesAsync();
            return chapter;
        }

        public async Task<Chapter> UpdateChapterAsync(Chapter chapter)
        {
            // Update Chapter
            _context.Entry(chapter).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return chapter;
        }

        public async Task<Chapter> DeleteChapterAsync(int id)
        {
            // Delete Chapter
            var chapter = await _context.Chapter.FindAsync(id);
            _context.Chapter.Remove(chapter);
            await _context.SaveChangesAsync();
            return chapter;
        }
        #endregion
    }
}
