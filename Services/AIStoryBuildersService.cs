using AIStoryBuilders.Models;
using Microsoft.EntityFrameworkCore;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        private readonly AppMetadata _appMetadata;
        public AIStoryBuildersService(
            AppMetadata appMetadata)
        {
            _appMetadata = appMetadata;
        }
    }
}
