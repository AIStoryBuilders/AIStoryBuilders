using OpenAI;
using OpenAI.Chat;
using System.Net;
using OpenAI.Files;
using OpenAI.Models;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Text.Json;
using Newtonsoft.Json;
using static AIStoryBuilders.Model.OrchestratorMethods;
using Microsoft.Maui.Storage;
using static AIStoryBuilders.Pages.Memory;
using System.Collections.Generic;

namespace AIStoryBuilders.Model
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> CompleteStory(string NewStory, string SelectedModel)
        public async Task<string> CompleteStory(string NewStory, string SelectedModel)
        {
            LogService.WriteToLog("CompleteStory - Start");
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";

            ChatMessages = new List<ChatMessage>();

            // **** Create AIStoryBuildersDatabase.json
            // Store Tasks in the Database as an array in a single property
            // Store the Last Read Index as a Property in Database
            // Store Summary as a Property in the Database
            AIStoryBuildersDatabaseObject = new
            {
                CurrentTask = "Read Text",
                LastWordRead = 0,
                Summary = ""
            };

            // Save AIStoryBuildersDatabase.json
            AIStoryBuildersDatabase objAIStoryBuildersDatabase = new AIStoryBuildersDatabase();
            objAIStoryBuildersDatabase.WriteFile(AIStoryBuildersDatabaseObject);

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization));

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // *****************************************************
            dynamic Databasefile = AIStoryBuildersDatabaseObject;

            // Get Background Text - Perform vector search using NewStory
            List<(string, float)> SearchResults = await SearchMemory(NewStory, 5);

            // Create a single string from the first colum of SearchResults
            string BackgroundText = string.Join(",", SearchResults.Select(x => x.Item1));

            // Update System Message
            SystemMessage = CreateSystemMessageStory(NewStory, BackgroundText);

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
                model: SelectedModel,
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

        #region private string CreateSystemMessageStory(string paramNewText, string paramBackgroundText)
        private string CreateSystemMessageStory(string paramNewText, string paramBackgroundText)
        {
            return "You are a program that will write a paragraph to continue a story starting " +
                   "with the content in ###New Text###. Write the paragraph \n" +
                   "only using information from ###New Text### and ###Background Text###.\n" +
                   "Only respond with a paragraph that completes the story nothing else.\n" +
                   "Only use information from ###New Text### and ###Background Text###.\n" +
                   $"###New Text### is: {paramNewText}\n" +
                   $"###Background Text### is: {paramBackgroundText}\n";
        }
        #endregion
    }
}
