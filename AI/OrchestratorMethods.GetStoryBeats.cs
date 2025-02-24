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
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> GetStoryBeats(string paramParagraph)
        public async Task<string> GetStoryBeats(string paramParagraph)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";
            string GPTModel = SettingsService.AIModel;

            LogService.WriteToLog($"GetStoryBeats using {GPTModel} - Start");

            // Create a new OpenAIClient object
            IChatClient api = CreateOpenAIClient();

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // Update System Message
            SystemMessage = CreateStoryBeats(paramParagraph);

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
                responseFormat: ChatResponseFormat.Text);

            ChatResponseResult = await api.ChatEndpoint.GetCompletionAsync(FinalChatRequest);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokens} - ChatResponseResult - {ChatResponseResult.FirstChoice.Message.Content}");

            return ChatResponseResult;
        }
        #endregion

        // Methods

        #region private string CreateStoryBeats(string paramParagraphContent)
        private string CreateStoryBeats(string paramParagraphContent)
        {
            return "You are a function that will produce only simple text. \n" +
            "Please analyze a paragraph of text (given as #paramParagraphContent). \n" +
            "#1 Create story beats for the paragraph. \n" +
            "#2 Output only the story beats, nothing else. \n" +
            $"### This is the content of #paramParagraphContent: {paramParagraphContent} \n";
        }
        #endregion
    }
}