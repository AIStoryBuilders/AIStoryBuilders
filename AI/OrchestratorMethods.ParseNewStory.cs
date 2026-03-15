using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIStoryBuilders.Model;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> ParseNewStory(string paramStoryTitle, string paramStoryText, string GPTModel)
        public async Task<string> ParseNewStory(string paramStoryTitle, string paramStoryText, string GPTModel)
        {
            LogService.WriteToLog($"ParseNewStory using {GPTModel} - Start");

            IChatClient api = CreateOpenAIClient();

            // Trim paramStoryText to 10000 words (so we don't run out of tokens)
            paramStoryText = OrchestratorMethods.TrimToMaxWords(paramStoryText, 10000);

            // Build prompt messages
            var promptService = new PromptTemplateService();
            var messages = promptService.BuildMessages(
                PromptTemplateService.Templates.ParseNewStory_System,
                PromptTemplateService.Templates.ParseNewStory_User,
                new Dictionary<string, string>
                {
                    ["StoryTitle"] = paramStoryTitle,
                    ["StoryText"] = paramStoryText
                });

            LogService.WriteToLog($"Prompt token estimate: {TokenEstimator.EstimateTokens(messages)}");

            var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, GPTModel);

            var result = await LlmCallHelper.CallLlmWithRetry<string>(
                api,
                messages,
                options,
                jObj => jObj.ToString(),
                LogService);

            return result ?? "{}";
        }
        #endregion
    }
}
