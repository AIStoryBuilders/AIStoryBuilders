using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        private readonly AppMetadata _appMetadata;
        private LogService LogService { get; set; }

        private OrchestratorMethods OrchestratorMethods { get; set; }

        public string BasePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders";
        public AIStoryBuildersService(
            AppMetadata appMetadata,
            LogService _LogService,
            OrchestratorMethods _OrchestratorMethods)
        {
            _appMetadata = appMetadata;
            LogService = _LogService;
            OrchestratorMethods = _OrchestratorMethods;
        }
    }
}
