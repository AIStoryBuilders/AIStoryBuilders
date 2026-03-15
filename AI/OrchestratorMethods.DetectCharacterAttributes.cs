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
        #region public async Task<List<SimpleCharacterSelector>> DetectCharacterAttributes(Paragraph objParagraph, List<Models.Character> colCharacters, string objDetectionType)
        public async Task<List<SimpleCharacterSelector>> DetectCharacterAttributes(Paragraph objParagraph, List<Models.Character> colCharacters, string objDetectionType)
        {
            string GPTModel = SettingsService.AIModel;

            LogService.WriteToLog($"DetectCharacterAttributes using {GPTModel} - Start");

            IChatClient api = CreateOpenAIClient();

            // Serialize the Characters to JSON
            var SimpleCharacters = ProcessCharacters(colCharacters);
            string json = CharacterJsonSerializer.Serialize(SimpleCharacters);

            // Build prompt messages
            var promptService = new PromptTemplateService();
            var messages = promptService.BuildMessages(
                PromptTemplateService.Templates.DetectCharacterAttributes_System,
                PromptTemplateService.Templates.DetectCharacterAttributes_User,
                new Dictionary<string, string>
                {
                    ["ParagraphContent"] = objParagraph.ParagraphContent,
                    ["CharacterJSON"] = json
                });

            LogService.WriteToLog($"Prompt token estimate: {TokenEstimator.EstimateTokens(messages)}");

            var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, GPTModel);

            List<string> colAllowedTypes = new List<string> { "Appearance", "Goals", "History", "Aliases", "Facts" };

            var result = await LlmCallHelper.CallLlmWithRetry<List<SimpleCharacterSelector>>(
                api,
                messages,
                options,
                jObj =>
                {
                    var colCharacterOutput = new List<SimpleCharacterSelector>();
                    var characters = jObj["characters"];
                    if (characters != null)
                    {
                        foreach (var character in characters)
                        {
                            string CharacterName = character["name"]?.ToString() ?? "";

                            // If the CharacterName is not in the list of colCharacters, skip it
                            if (!colCharacters.Any(x => x.CharacterName == CharacterName))
                                continue;

                            // Only add character element in "New Character" mode
                            if (objDetectionType == "New Character")
                            {
                                colCharacterOutput.Add(new SimpleCharacterSelector
                                {
                                    CharacterDisplay = $"Add Character - {CharacterName}",
                                    CharacterValue = $"{CharacterName}|{objDetectionType}||"
                                });
                            }

                            var descriptions = character["descriptions"];
                            if (descriptions != null)
                            {
                                foreach (var description in descriptions)
                                {
                                    string description_type = description["description_type"]?.ToString() ?? "";
                                    string description_text = description["description"]?.ToString() ?? "";

                                    if (!colAllowedTypes.Contains(description_type))
                                        continue;

                                    colCharacterOutput.Add(new SimpleCharacterSelector
                                    {
                                        CharacterDisplay = $"{CharacterName} - ({description_type}) {description_text}",
                                        CharacterValue = $"{CharacterName}|{objDetectionType}|{description_type}|{description_text}"
                                    });
                                }
                            }
                        }
                    }
                    return colCharacterOutput;
                },
                LogService);

            return result ?? new List<SimpleCharacterSelector>();
        }
        #endregion

        // Utility

        #region  public static class CharacterJsonSerializer
        public static class CharacterJsonSerializer
        {
            public static string Serialize(List<SimpleCharacter> characters)
            {
                return System.Text.Json.JsonSerializer.Serialize(characters, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                });
            }
        }
        #endregion

        #region public List<SimpleCharacter> ProcessCharacters(List<Models.Character> inputCharacters)
        public List<SimpleCharacter> ProcessCharacters(List<Models.Character> inputCharacters)
        {
            return inputCharacters.Select(character => new SimpleCharacter
            {
                CharacterName = character.CharacterName,
                CharacterBackground = character.CharacterBackground.Select(bg => new SimpleCharacterBackground
                {
                    Type = bg.Type,
                    Description = bg.Description
                }).ToList()
            }).ToList();
        }
        #endregion 
    }
}
