using Newtonsoft.Json;

namespace AIStoryBuilders.Model
{
    public class SettingsService
    {
        // Properties
        public string Organization { get; set; }
        public string ApiKey { get; set; }

        // Constructor
        public SettingsService() 
        {
            ReloadSettings();
        }

        public void ReloadSettings()
        {
            // Get OpenAI API key from appsettings.json
            // AIStoryBuilders Directory
            var AIStoryBuildersSettingsPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersSettings.config";

            string AIStoryBuildersSettings = "";

            // Open the file to get existing content
            using (var streamReader = new StreamReader(AIStoryBuildersSettingsPath))
            {
                AIStoryBuildersSettings = streamReader.ReadToEnd();
            }

            // Convert the JSON to a dynamic object
            dynamic AIStoryBuildersSettingsObject = JsonConvert.DeserializeObject(AIStoryBuildersSettings);

            Organization = AIStoryBuildersSettingsObject.OpenAIServiceOptions.Organization;
            ApiKey = AIStoryBuildersSettingsObject.OpenAIServiceOptions.ApiKey;
        }
    }
}