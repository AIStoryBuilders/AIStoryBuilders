using Newtonsoft.Json;
using OpenAI.Files;

namespace AIStoryBuilders.Model
{
    public class LogService
    {
        // Properties
        public string[] AIStoryBuildersLog { get; set; }

        // Constructor
        public LogService()
        {
            loadLog();
        }

        public void loadLog()
        {
            var AIStoryBuildersLogPath =
            $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersLog.csv";

            // Read the lines from the .csv file
            using (var file = new System.IO.StreamReader(AIStoryBuildersLogPath))
            {
                AIStoryBuildersLog = file.ReadToEnd().Split('\n');
                if (AIStoryBuildersLog[AIStoryBuildersLog.Length - 1].Trim() == "")
                {
                    AIStoryBuildersLog = AIStoryBuildersLog.Take(AIStoryBuildersLog.Length - 1).ToArray();
                }
            }
        }

        public void WriteToLog(string LogText)
        {
            // Open the file to get existing content
            var AIStoryBuildersLogPath =
                $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersLog.csv";

            using (var file = new System.IO.StreamReader(AIStoryBuildersLogPath))
            {
                AIStoryBuildersLog = file.ReadToEnd().Split('\n');

                if (AIStoryBuildersLog[AIStoryBuildersLog.Length - 1].Trim() == "")
                {
                    AIStoryBuildersLog = AIStoryBuildersLog.Take(AIStoryBuildersLog.Length - 1).ToArray();
                }
            }

            // If log has more than 1000 lines, keep only the recent 1000 lines
            if (AIStoryBuildersLog.Length > 1000)
            {
                AIStoryBuildersLog = AIStoryBuildersLog.Take(1000).ToArray();
            }

            // Append the text to csv file
            using (var streamWriter = new StreamWriter(AIStoryBuildersLogPath))
            {
                // Remove line breaks from the log text
                LogText = LogText.Replace("\n", " ");

                streamWriter.WriteLine(LogText);
                streamWriter.WriteLine(string.Join("\n", AIStoryBuildersLog));
            }
        }
    }
}