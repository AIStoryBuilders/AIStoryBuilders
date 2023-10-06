using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using Microsoft.EntityFrameworkCore;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        private readonly AppMetadata _appMetadata;
        public LogService LogService { get; set; }

        public string BasePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders";
        public AIStoryBuildersService(
            AppMetadata appMetadata,
            LogService _LogService)
        {
            _appMetadata = appMetadata;
            LogService = _LogService;
        }
    }
}
