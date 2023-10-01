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
        #region public async Task<string> CompleteChapter(string NewChapter, string SelectedModel)
        public async Task<string> CompleteChapter(string NewChapter, string SelectedModel)
        {
            LogService.WriteToLog("CompleteChapter - Start");
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

            // Get Background Text - Perform vector search using NewChapter
            List<(string, float)> SearchResults = await SearchMemory(NewChapter, 20);

            // Create a single string from the first colum of SearchResults
            string BackgroundText = string.Join(",", SearchResults.Select(x => x.Item1));

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Background retrieved: {BackgroundText.Split(' ').Length} words."));

            // Trim BackgroundText to 5000 words (so we don't run out of tokens)
            BackgroundText = OrchestratorMethods.TrimToMaxWords(BackgroundText, 10000);

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Background trimmed to: {BackgroundText.Split(' ').Length} words."));

            // Update System Message
            SystemMessage = CreateSystemMessageChapter(NewChapter, BackgroundText);

            chatPrompts = new List<Message>();

            chatPrompts.Insert(0,
            new Message(
                Role.System,
                SystemMessage
                )
            );

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Call ChatGPT..."));

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

        #region private string CreateSystemMessageChapter(string paramChapterDescription, string paramBackgroundText)
        private string CreateSystemMessageChapter(string paramChapterDescription, string paramBackgroundText)
        {
            return "You are a program that will write a complete 2000 word Chapter that follows " +
                   "the description in ###CHAPTER DESCRIPTION###. Write the Chapter \n" +
                   "only using information from ###Background Text###.\n" +
                   "Only respond with output that contains the Chapter nothing else.\n" +
                   "Only use information from ###CHAPTER DESCRIPTION### and ###Background Text###.\n" +
                   $"###CHAPTER DESCRIPTION### is: {paramChapterDescription}\n" +
                   $"###Background Text### is: {paramBackgroundText}\n";
        }
        #endregion
    }
}
