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
        #region public async Task<List<Models.Character>> DetectCharacters(Paragraph objParagraph)
        public async Task<List<Models.Character>> DetectCharacters(Paragraph objParagraph)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";
            string GPTModel = SettingsService.AIModel;

            LogService.WriteToLog($"Detect Characters using {GPTModel} - Start");

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization), null, new HttpClient() { Timeout = TimeSpan.FromSeconds(520) });

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // Update System Message
            SystemMessage = CreateDetectCharacters(objParagraph.ParagraphContent);

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

            List<Models.Character> colCharacterOutput = new List<Models.Character>();

            try
            {
                // Convert the JSON to a list of SimpleCharacters

                var JSONResult = ChatResponseResult.FirstChoice.Message.Content.ToString();

                dynamic data = JObject.Parse(JSONResult);

                foreach (var character in data.characters)
                {
                    string CharacterName = character.name.ToString();
                    colCharacterOutput.Add(new Models.Character { CharacterName = $"{CharacterName}", CharacterBackground = new List<CharacterBackground>() });
                }
            }
            catch (Exception ex)
            {
                LogService.WriteToLog($"Error - DetectCharacters: {ex.Message} {ex.StackTrace ?? ""}");
            }

            return colCharacterOutput;
        }
        #endregion

        // Methods

        #region private string CreateDetectCharacters(string paramParagraphContent)
        private string CreateDetectCharacters(string paramParagraphContent)
        {
            return "You are a function that will produce only JSON. \n" +
            "Please analyze a paragraph of text (given as #paramParagraphContent). \n" +
            "#1 Identify all characters, by name, mentioned in the paragraph. \n" +
            $"### This is the content of #paramParagraphContent: {paramParagraphContent} \n" +
            "Provide the results in the following JSON format: \n" +
            "{\n" +
            "\"characters\": [\n" +
            "{ \n" +
            "\"name\": \"[Name]\" \n" +
            "} \n" +
            "] \n" +
            "}";
        }
        #endregion
    }
}
