using AIStoryBuilders.Models;
using Microsoft.EntityFrameworkCore;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        private readonly AIStoryBuildersContext _context;
        private readonly AppMetadata _appMetadata;
        public AIStoryBuildersService(
            AIStoryBuildersContext context,
            AppMetadata appMetadata)
        {
            _context = context;
            _appMetadata = appMetadata;
        }
    }
}
