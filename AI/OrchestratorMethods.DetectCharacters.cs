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

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<List<Models.Character>> DetectCharacters(Paragraph objParagraph)
        public async Task<List<Models.Character>> DetectCharacters(Paragraph objParagraph)
        {
            string GPTModel = SettingsService.AIModel;

            LogService.WriteToLog($"DetectCharacters using {GPTModel} - Start");

            IChatClient api = CreateOpenAIClient();

            // Build prompt messages
            var promptService = new PromptTemplateService();
            var messages = promptService.BuildMessages(
                PromptTemplateService.Templates.DetectCharacters_System,
                PromptTemplateService.Templates.DetectCharacters_User,
                new Dictionary<string, string>
                {
                    ["ParagraphContent"] = objParagraph.ParagraphContent
                });

            LogService.WriteToLog($"Prompt token estimate: {TokenEstimator.EstimateTokens(messages)}");

            var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, GPTModel);

            var result = await LlmCallHelper.CallLlmWithRetry<List<Models.Character>>(
                api,
                messages,
                options,
                jObj =>
                {
                    var colCharacterOutput = new List<Models.Character>();
                    var characters = jObj["characters"];
                    if (characters != null)
                    {
                        foreach (var character in characters)
                        {
                            string CharacterName = character["name"]?.ToString() ?? "";
                            colCharacterOutput.Add(new Models.Character
                            {
                                CharacterName = CharacterName,
                                CharacterBackground = new List<CharacterBackground>()
                            });
                        }
                    }
                    return colCharacterOutput;
                },
                LogService);

            return result ?? new List<Models.Character>();
        }
        #endregion
    }
}
