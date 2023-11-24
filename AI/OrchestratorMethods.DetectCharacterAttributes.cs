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

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<List<SimpleCharacterSelector>> DetectCharacterAttributes(Paragraph objParagraph, List<Models.Character> colCharacters)
        public async Task<List<SimpleCharacterSelector>> DetectCharacterAttributes(Paragraph objParagraph, List<Models.Character> colCharacters)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";
            string GPTModel = "gpt-4-1106-preview";

            ChatMessages = new List<ChatMessage>();

            if (SettingsService.FastMode == true)
            {
                GPTModel = "gpt-3.5-turbo-1106";
            }

            LogService.WriteToLog($"Detect Character Attributes using {GPTModel} - Start");

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization), null, new HttpClient() { Timeout = TimeSpan.FromSeconds(520) });

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // *****************************************************
            dynamic Databasefile = AIStoryBuildersDatabaseObject;

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


            ChatResponseResult = await api.ChatEndpoint.GetCompletionAsync(FinalChatRequest);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokens} - ChatResponseResult - {ChatResponseResult.FirstChoice.Message.Content}");

            List<SimpleCharacterSelector> colCharacterOutput = new List<SimpleCharacterSelector>();

            try
            {
                // Convert the JSON to a list of SimpleCharacters

                var JSONResult = ChatResponseResult.FirstChoice.Message.Content.ToString();

                dynamic data = JObject.Parse(JSONResult);

                foreach (var character in data.characters)
                {
                    string CharacterName = character.name.ToString();
                    string CharacterAction = character.action.ToString();

                    if (CharacterAction == "New Character")
                    {
                        colCharacterOutput.Add(new SimpleCharacterSelector { CharacterDisplay = $"Add - {CharacterName}", CharacterValue = $"{CharacterName}|{CharacterAction}||" });
                    }

                    try
                    {
                        if (character.descriptions.Count > 0)
                        {
                            foreach (var description in character.descriptions)
                            {
                                string description_type = description.description_type.ToString();
                                string description_text = description.description.ToString();

                                colCharacterOutput.Add(new SimpleCharacterSelector { CharacterDisplay = $"{CharacterName} - ({description_type}) {description_text}", CharacterValue = $"{CharacterName}|{CharacterAction}|{description_type}|{description_text}" });
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

            // Loop through the characters and remove any existing characters from the list that do not have any descriptions
            List<SimpleCharacterSelector> colFinalCharacterOutput = new List<SimpleCharacterSelector>();

            foreach (var character in colCharacterOutput)
            {
                var objCharacter = character.CharacterValue.ToString().Split("|");

                var CharacterName = objCharacter[0];
                var Action = objCharacter[1];
                var description_type = objCharacter[2];
                var description_text = objCharacter[3];

                if (Action == "New Character")
                {
                    // Sometimes the LLM will think a character is new when it is not
                    // Only add a new character if they are not in the colCharacters collection
                    var objCharacterExists = colCharacters.Where(x => x.CharacterName == CharacterName).FirstOrDefault();

                    if (objCharacterExists == null)
                    {
                        colFinalCharacterOutput.Add(character);
                    }
                }
                else
                {
                    // If not a new character, only add if there is a description
                    if (description_type != "")
                    {
                        colFinalCharacterOutput.Add(character);
                    }
                }
            }

            return colFinalCharacterOutput;
        }
        #endregion

        // Methods

        #region private string CreateDetectCharacterAttributes(string paramParagraphContent, string CharacterJSON)
        private string CreateDetectCharacterAttributes(string paramParagraphContent, string CharacterJSON)
        {
            return "You are a function that will produce only JSON. \n" +
            "Please analyze a paragraph of text (given as #paramParagraphContent) and a JSON string representing a list of characters and their current attributes (given as #CharacterJSON). \n" +
            "#1 Identify any new characters, and or any new attributes for existing characters, mentioned in the paragraph that are not already present in #CharacterJSON. \n" +
            "#2 Parse the characters in #CharacterJSON to create a list of known characters. \n" +
            "#3 analyze #paramParagraphContent to extract character names. \n" +
            "#4 Compare these extracted names against the list of known characters derived from #CharacterJSON. \n" +
            "#5 New characters found in #paramParagraphContent but not in #CharacterJSON will be identified and their [Action] in the JSON will be set to New Character. \n" +
            "#6 If the character already exists in #CharacterJSON their [Action] in the JSON will be set to Existing Character. \n" +
            "#7 Only output each character once in the JSON. For existing characters only output new attributes for the character not found for the character in #CharacterJSON. \n" +
            "#8 Do not output any attributes for characters that is already in #CharacterJSON. \n" +
            $"### This is the content of #paramParagraphContent: {paramParagraphContent} \n" +
            $"### This is the content of #CharacterJSON: {CharacterJSON} \n" +
            "Provide the results in the following JSON format: \n" +
            "{\n" +
            "\"characters\": [\n" +
            "{ \n" +
            "\"name\": \"[Name]\", \n" +
            "\"action\": \"[Action]\", \n" +
            "\"enum\": [\"New Character\",\"Existing Character\",\"Add Attribute\"], \n" +
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
                return JsonSerializer.Serialize(characters, new JsonSerializerOptions
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
