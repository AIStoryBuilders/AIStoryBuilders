using AIStoryBuilders.Models;
using Microsoft.EntityFrameworkCore;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        public async Task<List<Story>> GetStorysAsync()
        {
            // Get Storys including Chapters           
            return await _context.Story
                .Include(story => story.Chapter)
                .AsNoTracking().ToListAsync();
        }
    }
}
