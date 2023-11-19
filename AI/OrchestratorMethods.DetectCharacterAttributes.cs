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

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<Message> DetectCharacterAttributes(Paragraph objParagraph, List<Models.Character> colCharacters)
        public async Task<Message> DetectCharacterAttributes(Paragraph objParagraph, List<Models.Character> colCharacters)
        {
            LogService.WriteToLog("Detect Character Attributes - Start");
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";

            ChatMessages = new List<ChatMessage>();

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization),null,new HttpClient() { Timeout= TimeSpan.FromSeconds(520) });

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
                model: "gpt-4-1106-preview",
                temperature: 0.0,
                topP: 1,
                frequencyPenalty: 0,
                presencePenalty: 0,
                responseFormat: ChatResponseFormat.Json);


            ChatResponseResult = await api.ChatEndpoint.GetCompletionAsync(FinalChatRequest);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokens} - ChatResponseResult - {ChatResponseResult.FirstChoice.Message.Content}");

            return ChatResponseResult.FirstChoice.Message;
        }
        #endregion

        // Methods

        #region private string CreateDetectCharacterAttributes(string paramParagraphContent, string CharacterJSON)
        private string CreateDetectCharacterAttributes(string paramParagraphContent, string CharacterJSON)
        {
            return "You are a function that will produce only JSON. \n" +
            "You are a function that will analyze a paragraph of text (given as #paramParagraphContent) \n" +
            "and a JSON string representing a list of characters (given as #CharacterJSON). \n" +
            "Identify any characters and or attributes mentioned in the paragraph \n" +
            "that are not already present in the JSON data. \n" +
            "Parse the characters in #CharacterJSON to create a list of known characters. \n" +
            "Then analyze #paramParagraphContent to extract character names. \n" +
            "Next, compare these extracted names against the list of known \n" +
            "characters derived from #CharacterJSON. \n" +
            "Characters found in #paramParagraphContent but not in #CharacterJSON will be identified. \n" +
            $"### This is the content of #paramParagraphContent: {paramParagraphContent} \n" +
            $"### This is the content of #CharacterJSON: {CharacterJSON} \n" +
            "Provide the results in the following JSON format: \n" +
            "\"characters\": [\n" +
            "{ \n" +
            "\"name\": name, \n" +
            "\"descriptions\": [\n" +
            "{ \n" +
            "\"description_type\": description_type, \n" +
            "\"enum\": [\"Appearance\",\"Goals\",\"History\",\"Aliases\",\"Facts\"], \n" +
            "\"description\": description \n" +
            "} \n" +
            "] \n" +
            "} \n" +
            "] \n";
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

        // Classes

        #region public class SimpleCharacter
        public class SimpleCharacter
        {
            public string CharacterName { get; set; }
            public List<SimpleCharacterBackground> CharacterBackground { get; set; }
        }
        #endregion

        #region public class SimpleCharacterBackground
        public class SimpleCharacterBackground
        {
            public string Type { get; set; }
            public string Description { get; set; }
        } 
        #endregion
    }
}
