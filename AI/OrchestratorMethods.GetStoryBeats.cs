using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
using OpenAI.Chat;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIStoryBuilders.Models;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using OpenAI.Moderations;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> GetStoryBeats(string paramParagraph)
        public async Task<string> GetStoryBeats(string paramParagraph)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";
            string GPTModel = SettingsService.AIModel;

            LogService.WriteToLog($"GetStoryBeats using {GPTModel} - Start");

            // Create a new OpenAIClient object
            IChatClient api = CreateOpenAIClient();

            // Update System Message
            SystemMessage = CreateStoryBeats(paramParagraph);

            LogService.WriteToLog($"Prompt: {SystemMessage}");

            var ChatResponseResult = await api.CompleteAsync(SystemMessage);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokenCount} - ChatResponseResult - {ChatResponseResult.Choices.FirstOrDefault().Text}");

            var JSONResult = ExtractJson(ChatResponseResult.Choices.FirstOrDefault().Text);

            return JSONResult;
        }
        #endregion

        // Methods

        #region private string CreateStoryBeats(string paramParagraphContent)
        private string CreateStoryBeats(string paramParagraphContent)
        {
            return "You are a function that will produce only simple text. \n" +
            "Please analyze a paragraph of text (given as #paramParagraphContent). \n" +
            "#1 Create story beats for the paragraph. \n" +
            "#2 Output only the story beats, nothing else. \n" +
            $"### This is the content of #paramParagraphContent: {paramParagraphContent} \n";
        }
        #endregion
    }
}