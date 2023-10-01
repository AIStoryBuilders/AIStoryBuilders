using Newtonsoft.Json;
using OpenAI.Files;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIStoryBuilders.Model
{
    public class AIStoryBuildersDatabase
    {
        // Constructor
        public AIStoryBuildersDatabase() { }

        public string ReadFile()
        {
            string response;
            string folderPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders";
            string filePath = Path.Combine(folderPath, "AIStoryBuildersDatabase.json");

            // Open the file to get existing content
            using (var streamReader = new StreamReader(filePath))
            {
                response = streamReader.ReadToEnd();
            }

            return response;
        }

        public dynamic ReadFileDynamic()
        {
            string FileContents;
            string folderPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders";
            string filePath = Path.Combine(folderPath, "AIStoryBuildersDatabase.json");

            // Open the file to get existing content
            using (var streamReader = new StreamReader(filePath))
            {
                FileContents = streamReader.ReadToEnd();
            }

            dynamic AIStoryBuildersDatabaseObject = JsonConvert.DeserializeObject(FileContents);

            return AIStoryBuildersDatabaseObject;
        }

        public async Task WriteFile(dynamic AIStoryBuildersDatabaseObject)
        {
            string folderPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders";
            string filePath = Path.Combine(folderPath, "AIStoryBuildersDatabase.json");

            // Convert the dynamic object back to JSON
            var AIStoryBuildersSettings = JsonConvert.SerializeObject(AIStoryBuildersDatabaseObject, Formatting.Indented);

            // Write the JSON to the file
            using (var streamWriter = new StreamWriter(filePath))
            {
                await streamWriter.WriteAsync(AIStoryBuildersSettings);
            }
        }

        public async Task WriteFile(string AIStoryBuildersDatabaseContent)
        {
            string folderPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders";
            string filePath = Path.Combine(folderPath, "AIStoryBuildersDatabase.json");

            // Write the JSON to the file
            using (var streamWriter = new StreamWriter(filePath))
            {
                await streamWriter.WriteAsync(AIStoryBuildersDatabaseContent);
            }
        }
    }
}