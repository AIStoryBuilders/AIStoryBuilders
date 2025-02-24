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
using Microsoft.Extensions.AI;

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
            IChatClient api = CreateOpenAIClient(GPTModel);

            // Update System Message
            SystemMessage = CreateSystemMessageCleanJSON(JSON);

            LogService.WriteToLog($"Prompt: {SystemMessage}");

            var ChatResponseResult = await api.CompleteAsync(SystemMessage);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokenCount} - ChatResponseResult - {ChatResponseResult.Choices.FirstOrDefault().Text}");

            return ChatResponseResult.Choices.FirstOrDefault().Text;
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
