using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
using OpenAI.Chat;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI.Moderations;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<ChatCompletion> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel)
        public async Task<Microsoft.Extensions.AI.ChatCompletion> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";

            LogService.WriteToLog($"CreateNewChapters using {GPTModel} - Start");

            // Create a new OpenAIClient object
            IChatClient api = CreateOpenAIClient();
           
            // Update System Message
            SystemMessage = CreateSystemMessageCreateNewChapters(JSONNewStory, ChapterCount);
           
            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Calling ChatGPT...", 70));

            var ChatResponseResult = await api.CompleteAsync(SystemMessage);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokenCount} - ChatResponseResult - {ChatResponseResult.Choices.FirstOrDefault().Text}");

            return ChatResponseResult;
        }
        #endregion

        // Methods

        #region private string CreateSystemMessageCreateNewChapters(string paramJSONNewStory, string paramChapterCount)
        private string CreateSystemMessageCreateNewChapters(string paramJSONNewStory, string paramChapterCount)
        {
            return "Given a story with the following structure: \n" +
                    "[ \n" +
                    $"{paramJSONNewStory} \n" +
                    "] \n" +
                    "Using only this information please: \n" +
                    $"#1 Create {paramChapterCount} chapters in a format like this: Chapter1, Chapter2, Chapter3. \n" +
                    "#2 A short chapter_synopsis description. Format this in story beats in a format like this: #Beat 1 - Something happens. #Beat 2 - The next things happens. #Beat 3 - Another thing happens. \n" +
                    "#3 A short 200 word first paragraph for each chapter. \n" +
                    "#4 A single timeline_name for each paragraph. \n" +
                    "#5 The list of character names that appear in each paragraph. \n" +
                    "Output JSON nothing else. \n" +
                    "Provide the results in the following JSON format: \n" +
                    "{ \n" +
                    "\"chapter\": [\n" +
                    "{ \n" +
                    "\"chapter_name\": chapter_name, \n" +
                    "\"chapter_synopsis\": chapter_synopsis, \n" +
                    "\"paragraphs\": [\n" +
                    "{ \n" +
                    "\"contents\": contents, \n" +
                    "\"location_name\": location_name, \n" +
                    "\"timeline_name\": timeline_name, \n" +
                    "\"character_names\": [character_names] \n" +
                    "} \n" +
                    "] \n" +
                    "} \n";
        }
        #endregion
    }
}
