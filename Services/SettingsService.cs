using Newtonsoft.Json;
using OpenAI.Files;

namespace AIStoryBuilders.Model
{
    public class SettingsService
    {
        // Properties
        public string Organization { get; set; }
        public string ApiKey { get; set; }
        public string AIModel { get; set; }
        public string AIType { get; set; }
        public string Endpoint { get; set; }
        public string ApiVersion { get; set; }

        // Constructor
        public SettingsService() 
        {
            LoadSettings();
        }

        public void LoadSettings()
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

            if (AIStoryBuildersSettingsObject.ApplicationSettings.AIType == null || AIStoryBuildersSettingsObject.ApplicationSettings.AIType == "")
            {
                AIStoryBuildersSettingsObject.ApplicationSettings.AIType = "OpenAI";
            }

            Organization = AIStoryBuildersSettingsObject.OpenAIServiceOptions.Organization;
            ApiKey = AIStoryBuildersSettingsObject.OpenAIServiceOptions.ApiKey;
            AIModel = AIStoryBuildersSettingsObject.ApplicationSettings.AIModel;
            AIType = AIStoryBuildersSettingsObject.ApplicationSettings.AIType;
            Endpoint = AIStoryBuildersSettingsObject.ApplicationSettings.Endpoint;
            ApiVersion = AIStoryBuildersSettingsObject.ApplicationSettings.ApiVersion;
        }

        public async Task SaveSettings(string paramOrganization, string paramApiKey, string paramAIModel, string paramAIType, string paramEndpoint, string paramApiVersion)
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

            // Update the dynamic object
            AIStoryBuildersSettingsObject.OpenAIServiceOptions.Organization = paramOrganization;
            AIStoryBuildersSettingsObject.OpenAIServiceOptions.ApiKey = paramApiKey;
            AIStoryBuildersSettingsObject.ApplicationSettings.AIModel = paramAIModel;
            AIStoryBuildersSettingsObject.ApplicationSettings.AIType = paramAIType;
            AIStoryBuildersSettingsObject.ApplicationSettings.Endpoint = paramEndpoint;
            AIStoryBuildersSettingsObject.ApplicationSettings.ApiVersion = paramApiVersion;

            // Convert the dynamic object back to JSON
            AIStoryBuildersSettings = JsonConvert.SerializeObject(AIStoryBuildersSettingsObject, Formatting.Indented);

            // Write the JSON back to the file
            using (var streamWriter = new StreamWriter(AIStoryBuildersSettingsPath))
            {
                await streamWriter.WriteAsync(AIStoryBuildersSettings);
            }

            // Update the properties
            Organization = paramOrganization;
            ApiKey = paramApiKey;
            AIModel = paramAIModel;
            AIType = paramAIType;
            Endpoint = paramEndpoint;
            ApiVersion = paramApiVersion;
        }
    }
}