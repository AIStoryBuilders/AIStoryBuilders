using Newtonsoft.Json;

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
            var AIStoryBuildersSettingsPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersSettings.config";

            string AIStoryBuildersSettings = "";

            using (var streamReader = new StreamReader(AIStoryBuildersSettingsPath))
            {
                AIStoryBuildersSettings = streamReader.ReadToEnd();
            }

            dynamic AIStoryBuildersSettingsObject = JsonConvert.DeserializeObject(AIStoryBuildersSettings);

            // Backward compat: default AIType to "OpenAI" if missing
            if (AIStoryBuildersSettingsObject.ApplicationSettings.AIType == null ||
                (string)AIStoryBuildersSettingsObject.ApplicationSettings.AIType == "")
            {
                AIStoryBuildersSettingsObject.ApplicationSettings.AIType = "OpenAI";
            }

            Organization = AIStoryBuildersSettingsObject.OpenAIServiceOptions.Organization;
            ApiKey = AIStoryBuildersSettingsObject.OpenAIServiceOptions.ApiKey;
            AIModel = AIStoryBuildersSettingsObject.ApplicationSettings.AIModel;
            AIType = AIStoryBuildersSettingsObject.ApplicationSettings.AIType;

            // Endpoint and ApiVersion are optional (Azure OpenAI only)
            try { Endpoint = AIStoryBuildersSettingsObject.ApplicationSettings.Endpoint; } catch { Endpoint = ""; }
            try { ApiVersion = AIStoryBuildersSettingsObject.ApplicationSettings.ApiVersion; } catch { ApiVersion = ""; }
        }

        public async Task SaveSettings(string paramOrganization, string paramApiKey, string paramAIModel, string paramAIType, string paramEndpoint, string paramApiVersion)
        {
            var AIStoryBuildersSettingsPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersSettings.config";

            string AIStoryBuildersSettings = "";

            using (var streamReader = new StreamReader(AIStoryBuildersSettingsPath))
            {
                AIStoryBuildersSettings = streamReader.ReadToEnd();
            }

            dynamic AIStoryBuildersSettingsObject = JsonConvert.DeserializeObject(AIStoryBuildersSettings);

            AIStoryBuildersSettingsObject.OpenAIServiceOptions.Organization = paramOrganization;
            AIStoryBuildersSettingsObject.OpenAIServiceOptions.ApiKey = paramApiKey;
            AIStoryBuildersSettingsObject.ApplicationSettings.AIModel = paramAIModel;
            AIStoryBuildersSettingsObject.ApplicationSettings.AIType = paramAIType;
            AIStoryBuildersSettingsObject.ApplicationSettings.Endpoint = paramEndpoint;
            AIStoryBuildersSettingsObject.ApplicationSettings.ApiVersion = paramApiVersion;

            AIStoryBuildersSettings = JsonConvert.SerializeObject(AIStoryBuildersSettingsObject, Formatting.Indented);

            using (var streamWriter = new StreamWriter(AIStoryBuildersSettingsPath))
            {
                await streamWriter.WriteAsync(AIStoryBuildersSettings);
            }

            Organization = paramOrganization;
            ApiKey = paramApiKey;
            AIModel = paramAIModel;
            AIType = paramAIType;
            Endpoint = paramEndpoint;
            ApiVersion = paramApiVersion;
        }
    }
}