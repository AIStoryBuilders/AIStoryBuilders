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

namespace AIStoryBuilders.Model
{
    public partial class OrchestratorMethods
    {
        #region public async Task<bool> LoadStory(string Filename, int intMaxLoops, int intChunkSize)
        public async Task<bool> LoadStory(string Filename, int intMaxLoops, int intChunkSize)
        {
            LogService.WriteToLog("LoadStory - Start");

            bool StoryLoaded = false;
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";
            int TotalTokens = 0;

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

            // Call ChatGPT
            int CallCount = 0;

            // We need to start a While loop
            bool ChatGPTCallingComplete = false;
            int StartWordIndex = 0;

            while (!ChatGPTCallingComplete)
            {
                // Read Text
                var CurrentText = await ExecuteRead(Filename, StartWordIndex, intChunkSize);

                // *****************************************************
                dynamic Databasefile = AIStoryBuildersDatabaseObject;

                // Update System Message
                SystemMessage = CreateSystemMessageLoadStory(CurrentText);

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
                    model: "gpt-3.5-turbo",
                    temperature: 0.0,
                    topP: 1,
                    frequencyPenalty: 0,
                    presencePenalty: 0);

                ChatResponseResult = await api.ChatEndpoint.GetCompletionAsync(FinalChatRequest);

                var ChatResponseContent = ChatResponseResult.FirstChoice.Message.Content;

                // Create a Vector database entry 
                if (ChatResponseContent != "")
                {
                    // *******************************************************                   
                    // Create a Vector database entry for each Character summary found
                    await CreateVectorEntry(ChatResponseContent);
                }

                // Update the Summary
                Summary = Summary + ChatResponseContent + "\n\n";

                // Update the total number of tokens used by the API
                TotalTokens = TotalTokens + ChatResponseResult.Usage.TotalTokens ?? 0;

                LogService.WriteToLog($"Iteration: {CallCount} - TotalTokens: {TotalTokens} - result.FirstChoice.Message - {ChatResponseResult.FirstChoice.Message}");

                if (Databasefile.CurrentTask == "Read Text")
                {
                    // Keep looping
                    ChatGPTCallingComplete = false;
                    CallCount = CallCount + 1;
                    StartWordIndex = Databasefile.LastWordRead;

                    // Update the AIStoryBuildersDatabase.json file
                    AIStoryBuildersDatabaseObject = new
                    {
                        CurrentTask = "Read Text",
                        LastWordRead = Databasefile.LastWordRead,
                        Summary = ChatResponseResult.FirstChoice.Message.Content
                    };

                    // Check if we have exceeded the maximum number of calls
                    if (CallCount > intMaxLoops)
                    {
                        // Break out of the loop
                        ChatGPTCallingComplete = true;
                        LogService.WriteToLog($"* Breaking out of loop * Iteration: {CallCount}");
                        ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Break out of the loop - Iteration: {CallCount}", 5));
                    }
                    else
                    {
                        ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Continue to Loop - Iteration: {CallCount}", 5));
                    }
                }
                else
                {
                    // Break out of the loop
                    ChatGPTCallingComplete = true;
                    LogService.WriteToLog($"Iteration: {CallCount}");
                    ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Break out of the loop - Iteration: {CallCount}", 5));
                }
            }

            // *****************************************************
            // Save AIStoryBuildersDatabase.json
            objAIStoryBuildersDatabase.WriteFile(AIStoryBuildersDatabaseObject);

            StoryLoaded = true;

            LogService.WriteToLog($"StoryLoaded - {StoryLoaded}");
            return StoryLoaded;
        }
        #endregion

        // Methods

        #region private string CreateSystemMessageLoadStory(string paramNewText)
        private string CreateSystemMessageLoadStory(string paramNewText)
        {
            return "You are a program that will produce a summary of the content of ###New Text###.\n" +
                    "Only respond with the contents of the summary nothing else.\n" +
                    "Always output complete sentences.\n" +
                    "Only respond with the contents of the summary nothing else.\n" +
                    $"###New Text### is: {paramNewText}\n";
        }
        #endregion
    }
}