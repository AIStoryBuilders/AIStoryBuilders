using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
using OpenAI.Chat;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI.Moderations;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> CleanJSON(string JSON, string GPTModel)
        public async Task<string> CleanJSON(string JSON, string GPTModel)
        {
            string SystemMessage = "";

            LogService.WriteToLog($"Clean JSON using {GPTModel} - Start");

            // Create a new OpenAIClient object
            // with the provided API key and organization
            OpenAIClient api = CreateOpenAIClient();

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // Update System Message
            SystemMessage = CreateSystemMessageCleanJSON(JSON);

            LogService.WriteToLog($"Prompt: {SystemMessage}");

            chatPrompts = new List<Message>();

            chatPrompts.Insert(0,
            new Message(
                Role.System,
                SystemMessage
                )
            );

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Calling ChatGPT to clean JSON...", 20));

            // Get a response from ChatGPT 
            var FinalChatRequest = new ChatRequest(
                chatPrompts,
                model: GPTModel,
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

        #region private string CreateSystemMessageCleanJSON(string paramJSON)
        private string CreateSystemMessageCleanJSON(string paramJSON)
        {
            return "Please correct this json to make it valid. Return only the valid json: \n" +
                    $"{paramJSON} \n";                    
        }
        #endregion
    }
}
