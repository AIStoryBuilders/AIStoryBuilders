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
using System.Collections.Generic;

namespace AIStoryBuilders.Model
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> ParseNewStory(string paramStoryTitle, string paramStoryText)
        public async Task<string> ParseNewStory(string paramStoryTitle, string paramStoryText)
        {
            LogService.WriteToLog("ParseNewStory - Start");
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

            // Trim paramStoryText to 10000 words (so we don't run out of tokens)
            paramStoryText = OrchestratorMethods.TrimToMaxWords(paramStoryText, 10000);

            // Update System Message
            SystemMessage = CreateSystemMessageParseNewStory(paramStoryTitle, paramStoryText);

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

        #region private string CreateSystemMessageParseNewStory(string paramStoryTitle, string paramStoryText)
        private string CreateSystemMessageParseNewStory(string paramStoryTitle, string paramStoryText)
        {
            return "Given a story titled: \n" +
                    "[ \n" +
                    $"{paramStoryTitle} \n" +
                    "] \n" +
                    "With the story text: \n" +
                    "[ \n" +
                    $"{paramStoryText} \n" +
                    "] \n" +
                    "Using only this information please identify: \n" +
                    "#1 Locations mentioned in the story with a short description of each location. \n" +
                    "#2 A short timelines name and a short sentance description to identify specific chronological events of the story. \n" +
                    "#3 Characters present in the story. \n" +
                    "#4 For each Character a description_type with a description and the timeline_name from timelines. \n" +
                    "Provide the results in the following JSON format: \n" +
                    "{ \n" +
                    "\"locations\":  \n" +
                    "\"name\": name, \n" +
                    "\"descriptions\": [descriptions] \n" +
                    "}, \n" +
                    "\"timelines\":  \n" +
                    "\"name\": name, \n" +
                    "\"description\": description \n" +
                    "}, \n" +
                    "{ \n" +
                    "\"characters\": \n" +
                    "{ \n" +
                    "\"name\": name, \n" +
                    "\"descriptions\": \n" +
                    "{ \n" +
                    "\"description_type\": description_type, \n" +
                    "\"enum\": [\"Appearance\",\"Goals\",\"History\",\"Aliases\",\"Facts\"], \n" +
                    "\"description\": description, \n" +
                    "\"timeline_name\": timeline_name \n" +
                    "} \n" +
                    "} \n";
        }
        #endregion
    }
}