using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
using OpenAI.Chat;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static AIStoryBuilders.Model.OrchestratorMethods;

namespace AIStoryBuilders.Model
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> CreateNewChapters(string JSONNewStory)
        public async Task<string> CreateNewChapters(string JSONNewStory)
        {
            LogService.WriteToLog("CreateNewChapters - Start");
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";

            ChatMessages = new List<ChatMessage>();

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization));

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // *****************************************************
            dynamic Databasefile = AIStoryBuildersDatabaseObject;

            // Update System Message
            SystemMessage = CreateSystemMessageCreateNewChapters(JSONNewStory);

            LogService.WriteToLog($"Prompt: {SystemMessage}");

            chatPrompts = new List<Message>();

            chatPrompts.Insert(0,
            new Message(
                Role.System,
                SystemMessage
                )
            );

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Calling ChatGPT...", 30));

            // Get a response from ChatGPT 
            var FinalChatRequest = new ChatRequest(
                chatPrompts,
                model: "gpt-4",
                temperature: 0.0,
                topP: 1,
                frequencyPenalty: 0,
                presencePenalty: 0);

            ChatResponseResult = await api.ChatEndpoint.GetCompletionAsync(FinalChatRequest);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokens} - ChatResponseResult - {ChatResponseResult.FirstChoice.Message.Content}");

            return ChatResponseResult.FirstChoice.Message.Content;
        }
        #endregion

        // Methods

        #region private string CreateSystemMessageCreateNewChapters(string paramJSONNewStory)
        private string CreateSystemMessageCreateNewChapters(string paramJSONNewStory)
        {
            return "Given a story with the following structure: \n" +
                    "[ \n" +
                    $"{paramJSONNewStory} \n" +
                    "] \n" +
                    "Using only this information please: \n" +
                    "#1 Create 3 chapters (Chapter1, Chapter2, Chapter3). \n" +
                    "#2 A short chapter_synopsis description. \n" +
                    "#3 A short first paragraph for each chapter. \n" +
                    "#4 A single timeline_name for each paragraph. \n" +
                    "#5 The list of character names that appear in each paragraph. \n" +
                    "Output JSON nothing else. \n" +
                    "Provide the results in the following JSON format: \n" +
                    "{ \n" +
                    "\"chapter\": [\n" +
                    "{ \n" +
                    "\"chapter_name\": chapter_name, \n" +
                    "\"chapter_synopsis\": chapter_synopsis, \n" +
                    "\"paragraphs\": [\n" +
                    "{ \n" +
                    "\"contents\": contents, \n" +
                    "\"location_name\": location_name, \n" +
                    "\"timeline_name\": timeline_name, \n" +
                    "\"character_names\": [character_names] \n" +
                    "} \n" +
                    "] \n" +
                    "} \n";
        }
        #endregion
    }
}
