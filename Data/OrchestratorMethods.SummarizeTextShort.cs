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

namespace AIStoryBuilders.Model
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> ReadTextShort(string Filename, int intMaxLoops, int intChunkSize)
        public async Task<string> ReadTextShort(string Filename, int intMaxLoops, int intChunkSize)
        {
            LogService.WriteToLog("ReadTextShort - Start");

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
                string CurrentSummary = AIStoryBuildersDatabaseObject.Summary ?? "";

                // Update System Message
                SystemMessage = CreateSystemMessage(CurrentSummary, CurrentText);

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
            // Clean up the final summary
            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Clean up the final summary"));
            string RawSummary = ChatResponseResult.FirstChoice.Message.Content;

            chatPrompts = new List<Message>();

            chatPrompts.Insert(0,
            new Message(
                Role.System,
                $"Format the following summary to break it up into paragraphs: {RawSummary}"
                )
            );

            // Get a response from ChatGPT 
            var chatRequest = new ChatRequest(
                chatPrompts,
                model: "gpt-3.5-turbo",
                temperature: 0.0,
                topP: 1,
                frequencyPenalty: 0,
                presencePenalty: 0);

            ChatResponseResult = await api.ChatEndpoint.GetCompletionAsync(chatRequest);

            // Save AIStoryBuildersDatabase.json
            objAIStoryBuildersDatabase.WriteFile(AIStoryBuildersDatabaseObject);

            LogService.WriteToLog($"result.FirstChoice.Message - {ChatResponseResult.FirstChoice.Message}");
            return ChatResponseResult.FirstChoice.Message;
        }
        #endregion

        // Methods

        #region private string CreateSystemMessage(string paramCurrentSummary, string paramNewText)
        private string CreateSystemMessage(string paramCurrentSummary, string paramNewText)
        {
            // The AI should keep this under 1000 words but here we will ensure it
            paramCurrentSummary = EnsureMaxWords(paramCurrentSummary, 1000);

            return "You are a program that will produce a summary not to exceed 1000 words. \n" +
                    "Only respond with the contents of the summary nothing else. \n" +
                    "Output a summary that combines the contents of ###Current Summary### with the additional content in ###New Text###. \n" +
                    "In the summary only use content from ###Current Summary### and ###New Text###. \n" +
                    "Only respond with the contents of the summary nothing else. \n" +
                    "Do not allow the summary to exceed 1000 words. \n" +
                    $"###Current Summary### is: {paramCurrentSummary}\n" +
                    $"###New Text### is: {paramNewText}\n";
        }
        #endregion

        #region public static string EnsureMaxWords(string paramCurrentSummary, int maxWords)
        public static string EnsureMaxWords(string paramCurrentSummary, int maxWords)
        {
            // Split the string by spaces to get words
            var words = paramCurrentSummary.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length <= maxWords)
            {
                // If the number of words is within the limit, return the original string
                return paramCurrentSummary;
            }

            // If the number of words exceeds the limit, return only the last 'maxWords' words
            return string.Join(" ", words.Reverse().Take(maxWords).Reverse());
        }
        #endregion
    }
}