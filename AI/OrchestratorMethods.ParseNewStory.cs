using OpenAI;
using OpenAI.Chat;
using System.Net;
using OpenAI.Files;
using OpenAI.Models;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Text.Json;
using Newtonsoft.Json;
using Microsoft.Maui.Storage;
using System.Collections.Generic;
using AIStoryBuilders.Model;
using OpenAI.Moderations;
using System.Threading;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<Message> ParseNewStory(string paramStoryTitle, string paramStoryText, string GPTModel)
        public async Task<string> ParseNewStory(string paramStoryTitle, string paramStoryText, string GPTModel)
        {            
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";

            LogService.WriteToLog($"ParseNewStory using {GPTModel} - Start");

            // Create a new OpenAIClient object
            IChatClient api = CreateOpenAIClient();

            // Trim paramStoryText to 10000 words (so we don't run out of tokens)
            paramStoryText = OrchestratorMethods.TrimToMaxWords(paramStoryText, 10000);

            // Update System Message
            SystemMessage = CreateSystemMessageParseNewStory(paramStoryTitle, paramStoryText);

            LogService.WriteToLog($"Prompt: {SystemMessage}");

            var ChatResponseResult = await api.CompleteAsync(SystemMessage);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokenCount} - ChatResponseResult - {ChatResponseResult.Choices.FirstOrDefault().Text}");

            return ChatResponseResult.Choices.FirstOrDefault().Text;
        }
        #endregion

        // Methods

        #region private string CreateSystemMessageParseNewStory(string paramStoryTitle, string paramStoryText)
        private string CreateSystemMessageParseNewStory(string paramStoryTitle, string paramStoryText)
        {
            return "Given a story titled: \n" +
                    $"[ {paramStoryTitle} ] \n" +
                    "With the story text: \n" +
                    $"[ {paramStoryText} ] \n" +
                    "Using only this information please identify: \n" +
                    "#1 Locations mentioned in the story with a short description of each location. \n" +
                    "#2 A short timeline's name and a short sentence description to identify specific chronological events of the story. \n" +
                    "#3 Characters present in the story. \n" +
                    "#4 For each Character a description_type with a description and the timeline_name from timelines. \n" +
                    "Provide the results in the following JSON format: \n" +
                    "{ \n" +
                    "  \"locations\": [{ \n" +
                    "    \"name\": \"name\", \n" +
                    "    \"description\": \"description\" \n" +
                    "  }], \n" +
                    "  \"timelines\": [{ \n" +
                    "    \"name\": \"name\", \n" +
                    "    \"description\": \"description\" \n" +
                    "  }], \n" +
                    "  \"characters\": [{ \n" +
                    "    \"name\": \"name\", \n" +
                    "    \"descriptions\": [{ \n" +
                    "      \"description_type\": \"description_type\", \n" +
                    "      \"enum\": [\"Appearance\", \"Goals\", \"History\", \"Aliases\", \"Facts\"], \n" +
                    "      \"description\": \"description\", \n" +
                    "      \"timeline_name\": \"timeline_name\" \n" +
                    "    }] \n" +
                    "  }] \n" +
                    "}";
        }
        #endregion
    }
}
