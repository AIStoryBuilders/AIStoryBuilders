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
        #region public async Task<List<string>> LoadCharacters(string Filename, int intMaxLoops, int intChunkSize)
        public async Task<List<string>> LoadCharacters(string Filename, int intMaxLoops, int intChunkSize)
        {
            LogService.WriteToLog("LoadCharacters - Start");

            string CharacterSummary = "";
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
                SystemMessage = CreateSystemMessageCharacters(CurrentText);

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

                var NamedCharactersFound = ChatResponseResult.FirstChoice.Message.Content;

                // *******************************************************
                // Update the Character Summary
                CharacterSummary = CombineAndSortLists(CharacterSummary, NamedCharactersFound);

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
                        ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Break out of the loop - Iteration: {CallCount}"));
                    }
                    else
                    {
                        ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Continue to Loop - Iteration: {CallCount}"));
                    }
                }
                else
                {
                    // Break out of the loop
                    ChatGPTCallingComplete = true;
                    LogService.WriteToLog($"Iteration: {CallCount}");
                    ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Break out of the loop - Iteration: {CallCount}"));
                }
            }

            // *****************************************************
            // Output final summary

            // Save AIStoryBuildersDatabase.json
            objAIStoryBuildersDatabase.WriteFile(AIStoryBuildersDatabaseObject);

            LogService.WriteToLog($"CharacterSummary - {CharacterSummary}");

            string[] CharacterSummaryArray = CharacterSummary.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            return CharacterSummaryArray.ToList();
        }
        #endregion

        // Methods

        #region private string CreateSystemMessageCharacters(string paramNewText)
        private string CreateSystemMessageCharacters(string paramNewText)
        {
            return "You are a program that will identify the names of the named characters in the content of ###New Text###.\n" +
                    "Only respond with the names of the named characters nothing else.\n" +
                    "Only list each character name once.\n" +
                    "List each character on a seperate line.\n" +
                    "Only respond with the names of the named characters nothing else.\n" +
                    $"###New Text### is: {paramNewText}\n";
        }
        #endregion
    }
}