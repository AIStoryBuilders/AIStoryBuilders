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
            return story;
        }

        public async Task<Story> UpdateStoryAsync(Story story)
        {
            // Update Story
            _context.Entry(story).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return story;
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
