using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIStoryBuilders.Models;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> GetStoryBeats(string paramParagraph)
        public async Task<string> GetStoryBeats(string paramParagraph)
        {
            string GPTModel = SettingsService.AIModel;

            LogService.WriteToLog($"GetStoryBeats using {GPTModel} - Start");

            IChatClient api = CreateOpenAIClient();

            // Build prompt messages
            var promptService = new PromptTemplateService();
            var messages = promptService.BuildMessages(
                PromptTemplateService.Templates.GetStoryBeats_System,
                PromptTemplateService.Templates.GetStoryBeats_User,
                new Dictionary<string, string>
                {
                    ["ParagraphContent"] = paramParagraph
                });

            LogService.WriteToLog($"Prompt token estimate: {TokenEstimator.EstimateTokens(messages)}");

            // GetStoryBeats returns plain text, not JSON - use plain options
            var options = new ChatOptions { ModelId = GPTModel };

            var result = await LlmCallHelper.CallLlmForText(
                api,
                messages,
                options,
                LogService);

            return result;
        }
        #endregion
    }
}