using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<ChatResponse> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel)
        public async Task<Microsoft.Extensions.AI.ChatResponse> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel)
        {
            LogService.WriteToLog($"CreateNewChapters using {GPTModel} - Start");

            IChatClient api = CreateOpenAIClient();

            // Build prompt messages
            var promptService = new PromptTemplateService();
            var messages = promptService.BuildMessages(
                PromptTemplateService.Templates.CreateNewChapters_System,
                PromptTemplateService.Templates.CreateNewChapters_User,
                new Dictionary<string, string>
                {
                    ["StoryJSON"] = JSONNewStory,
                    ["ChapterCount"] = ChapterCount
                });

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Calling AI provider...", 70));

            LogService.WriteToLog($"Prompt token estimate: {TokenEstimator.EstimateTokens(messages)}");

            var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, GPTModel);

            var response = await api.GetResponseAsync(messages, options);

            LogService.WriteToLog($"TotalTokens: {response.Usage?.TotalTokenCount} - ChatResponseResult - {response.Text}");

            return response;
        }
        #endregion
    }
}
