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

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<List<SimpleCharacterSelector>> DetectCharacterAttributes(Paragraph objParagraph, List<Models.Character> colCharacters, string objDetectionType)
        public async Task<List<SimpleCharacterSelector>> DetectCharacterAttributes(Paragraph objParagraph, List<Models.Character> colCharacters, string objDetectionType)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";
            string GPTModel = SettingsService.AIModel;

            LogService.WriteToLog($"Detect Character Attributes using {GPTModel} - Start");

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization), null, new HttpClient() { Timeout = TimeSpan.FromSeconds(520) });

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // Serialize the Characters to JSON
            var SimpleCharacters = ProcessCharacters(colCharacters);
            string json = CharacterJsonSerializer.Serialize(SimpleCharacters);

            // Update System Message
            SystemMessage = CreateDetectCharacterAttributes(objParagraph.ParagraphContent, json);

            LogService.WriteToLog($"Prompt: {SystemMessage}");

            chatPrompts = new List<Message>();

            chatPrompts.Insert(0,
            new Message(
                Role.System,
                SystemMessage
                )
            );

            // Get a response from ChatGPT 
            var FinalChatRequest = new ChatRequest(
                chatPrompts,
                model: GPTModel,
                temperature: 0.0,
                topP: 1,
                frequencyPenalty: 0,
                presencePenalty: 0,
                responseFormat: ChatResponseFormat.Json);

            // Check Moderation
            var ModerationResult = await api.ModerationsEndpoint.GetModerationAsync(SystemMessage);

            if (ModerationResult)
            {
                ModerationsResponse moderationsResponse = await api.ModerationsEndpoint.CreateModerationAsync(new ModerationsRequest(SystemMessage));

                // Serailize the ModerationsResponse
                string ModerationsResponseString = JsonConvert.SerializeObject(moderationsResponse.Results.FirstOrDefault().Categories);

                LogService.WriteToLog($"OpenAI Moderation flagged the content: [{SystemMessage}] as violating its policies: {ModerationsResponseString}");
                ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"WARNING! OpenAI Moderation flagged the content as violating its policies. See the logs for more details.", 30));
            }

            ChatResponseResult = await api.ChatEndpoint.GetCompletionAsync(FinalChatRequest);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokens} - ChatResponseResult - {ChatResponseResult.FirstChoice.Message.Content}");

            List<SimpleCharacterSelector> colCharacterOutput = new List<SimpleCharacterSelector>();

            try
            {
                // Convert the JSON to a list of SimpleCharacters

                List<string> colAllowedTypes = new List<string> { "Appearance", "Goals", "History", "Aliases", "Facts" };

                var JSONResult = ChatResponseResult.FirstChoice.Message.Content.ToString();

                dynamic data = JObject.Parse(JSONResult);

                foreach (var character in data.characters)
                {
                    string CharacterName = character.name.ToString();

                    // If the CharacterName is not in the list of colCharacters, then don't add it to the list
                    // The LLM added a new character even though it was not in the list of characters passed to it
                    if (colCharacters.Where(x => x.CharacterName == CharacterName).Count() == 0)
                    {
                        continue;
                    }

                    // We only create a Add character element if we are in "New Character" mode
                    if (objDetectionType == "New Character")
                    {
                        colCharacterOutput.Add(new SimpleCharacterSelector { CharacterDisplay = $"Add Character - {CharacterName}", CharacterValue = $"{CharacterName}|{objDetectionType}||" });
                    }

                    try
                    {
                        if (character.descriptions.Count > 0)
                        {
                            foreach (var description in character.descriptions)
                            {
                                // Only add the description if it is in the list of allowed types
                                if (colAllowedTypes.Contains(description.description_type.ToString()) == false)
                                {
                                    continue;
                                }

                                string description_type = description.description_type.ToString();
                                string description_text = description.description.ToString();

                                colCharacterOutput.Add(new SimpleCharacterSelector { CharacterDisplay = $"{CharacterName} - ({description_type}) {description_text}", CharacterValue = $"{CharacterName}|{objDetectionType}|{description_type}|{description_text}" });
                            }
                        }
                    }
                    catch
                    {
                        // Do nothing - sometimes there are no descriptions
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteToLog($"Error - DetectCharacterAttributes: {ex.Message} {ex.StackTrace ?? ""}");
            }            

            return colCharacterOutput;
        }
        #endregion

        // Methods

        #region private string CreateDetectCharacterAttributes(string paramParagraphContent, string CharacterJSON)
        private string CreateDetectCharacterAttributes(string paramParagraphContent, string CharacterJSON)
        {
            return "You are a function that will produce only JSON. \n" +
            "#1 Please analyze a paragraph of text (given as #paramParagraphContent) and a JSON string representing a list of characters and their current descriptions (given as #CharacterJSON). \n" +
            "#2 Output a new JSON containing only new descriptions. \n" +
            "#3 Do not output CharacterName not present in #CharacterJSON. \n" +
            "#4 Identify any new descriptions for each character in #CharacterJSON, mentioned in #paramParagraphContent that are not already present for the CharacterName in #CharacterJSON. \n" +
            "#5 Only output each character once in the JSON. \n" +
            "#6 Do not output any descriptions for any CharacterName that is already in #CharacterJSON for that CharacterName. \n" +
            $"### This is the content of #paramParagraphContent: {paramParagraphContent} \n" +
            $"### This is the content of #CharacterJSON: {CharacterJSON} \n" +
            "Provide the results in the following JSON format: \n" +
            "{\n" +
            "\"characters\": [\n" +
            "{ \n" +
            "\"name\": \"[Name]\", \n" +
            "\"descriptions\": [\n" +
            "{ \n" +
            "\"description_type\": \"[DescriptionType]\", \n" +
            "\"enum\": [\"Appearance\",\"Goals\",\"History\",\"Aliases\",\"Facts\"], \n" +
            "\"description\": \"[Description]\" \n" +
            "} \n" +
            "] \n" +
            "} \n" +
            "] \n" +
            "}";
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
                    WriteIndented = true, // for pretty printing; set to false for compact JSON
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles // to handle circular references
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
