using Newtonsoft.Json;
using OpenAI.Files;

namespace AIStoryBuilders.Model
{
    public class DatabaseService
    {
        // Properties
        public Dictionary<string, string> colAIStoryBuildersDatabase { get; set; }

        // Constructor
        public DatabaseService()
        {
            LoadDatabase();
        }

        public void LoadDatabase()
        {
            colAIStoryBuildersDatabase = new Dictionary<string, string>();

            // Get OpenAI API key from appDatabase.json
            // AIStoryBuilders Directory
            var AIStoryBuildersDatabasePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersDatabase.json";

            dynamic AIStoryBuildersDatabase;

            // Open the file to get existing content
            using (var streamReader = new StreamReader(AIStoryBuildersDatabasePath))
            {
                AIStoryBuildersDatabase = streamReader.ReadToEnd();
            }

            try
            {
                // Convert the JSON to a dynamic object
                dynamic AIStoryBuildersDatabaseObject = JsonConvert.DeserializeObject(AIStoryBuildersDatabase);

                foreach (var line in AIStoryBuildersDatabaseObject)
                {
                    try
                    {
                        var Key = line.Name;
                        var Value = line.Value.ToString();

                        colAIStoryBuildersDatabase.Add(Key, Value);
                    }
                    catch
                    {
                        // Skip the line if it's not valid JSON
                    }
                }
            }
            catch
            {
                // Skip the line if it's not valid JSON
            }            
        }

        public async Task SaveDatabase(Dictionary<string, string> paramColAIStoryBuildersDatabase)
        {
            // Get OpenAI API key from appDatabase.json
            // AIStoryBuilders Directory
            var AIStoryBuildersDatabasePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersDatabase.json";

            // Convert the dynamic object to JSON
            var AIStoryBuildersDatabase = JsonConvert.SerializeObject(paramColAIStoryBuildersDatabase, Formatting.Indented);

            // Write the JSON to the file
            using (var streamWriter = new StreamWriter(AIStoryBuildersDatabasePath))
            {
                await streamWriter.WriteAsync(AIStoryBuildersDatabase);
            }

            // Update the public property
            colAIStoryBuildersDatabase = paramColAIStoryBuildersDatabase;
        }
    }
}