using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIStoryBuilders.Models;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Microsoft.Extensions.AI;
using AIStoryBuilders.Services;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> WriteParagraph(JSONMasterStory objJSONMasterStory, AIPrompt paramAIPrompt, string GPTModel)
        public async Task<string> WriteParagraph(JSONMasterStory objJSONMasterStory, AIPrompt paramAIPrompt, string GPTModel)
        {
            LogService.WriteToLog($"WriteParagraph using {GPTModel} - Start");

            IChatClient api = CreateOpenAIClient();

            // Token-budget trimming
            var builder = new MasterStoryBuilder(GPTModel);
            objJSONMasterStory = builder.TrimToFit(
                objJSONMasterStory,
                PromptTemplateService.Templates.WriteParagraph_System,
                PromptTemplateService.Templates.WriteParagraph_User);

            // Build instructions
            string instructions = !string.IsNullOrWhiteSpace(paramAIPrompt.AIPromptText)
                ? paramAIPrompt.AIPromptText.Trim()
                : (string.IsNullOrEmpty(objJSONMasterStory.CurrentParagraph?.contents)
                    ? "Write the next paragraph in the chapter."
                    : "Continue from the last paragraph.");

            // Build prompt messages
            var promptService = new PromptTemplateService();
            var messages = promptService.BuildMessages(
                PromptTemplateService.Templates.WriteParagraph_System,
                PromptTemplateService.Templates.WriteParagraph_User,
                new Dictionary<string, string>
                {
                    ["StoryTitle"] = objJSONMasterStory.StoryTitle ?? "",
                    ["StoryStyle"] = objJSONMasterStory.StoryStyle ?? "",
                    ["StorySynopsis"] = objJSONMasterStory.StorySynopsis ?? "",
                    ["SystemMessage"] = objJSONMasterStory.SystemMessage ?? "",
                    ["CurrentChapter"] = System.Text.Json.JsonSerializer.Serialize(objJSONMasterStory.CurrentChapter),
                    ["PreviousParagraphs"] = System.Text.Json.JsonSerializer.Serialize(objJSONMasterStory.PreviousParagraphs),
                    ["CurrentLocation"] = System.Text.Json.JsonSerializer.Serialize(objJSONMasterStory.CurrentLocation),
                    ["CharacterList"] = System.Text.Json.JsonSerializer.Serialize(objJSONMasterStory.CharacterList),
                    ["RelatedParagraphs"] = System.Text.Json.JsonSerializer.Serialize(objJSONMasterStory.RelatedParagraphs),
                    ["CurrentParagraph"] = objJSONMasterStory.CurrentParagraph?.contents ?? "",
                    ["Instructions"] = instructions,
                    ["NumberOfWords"] = paramAIPrompt.NumberOfWords.ToString()
                });

            LogService.WriteToLog($"Prompt token estimate: {TokenEstimator.EstimateTokens(messages)}");

            var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, GPTModel);

            var result = await LlmCallHelper.CallLlmWithRetry<string>(
                api,
                messages,
                options,
                jObj => jObj["paragraph_content"]?.ToString() ?? "",
                LogService);

            return result ?? "";
        }
        #endregion
    }
}
