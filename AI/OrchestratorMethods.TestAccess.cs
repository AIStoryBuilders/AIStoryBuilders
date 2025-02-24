using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
using OpenAI.Chat;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<bool> TestAccess(string GPTModel)
        public async Task<bool> TestAccess(string GPTModel)
        {
            string SystemMessage = "";

            LogService.WriteToLog($"TestAccess using {GPTModel} - Start");

            IChatClient api = CreateOpenAIClient();

            // Update System Message
            SystemMessage = "Please return the following as json: \"This is successful\" in this format {\r\n  'message': message\r\n}";

            LogService.WriteToLog($"Prompt: {SystemMessage}");

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Calling ChatGPT to test access...", 5));

            var ChatResponseResult = await api.CompleteAsync(SystemMessage);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokenCount} - ChatResponseResult - {ChatResponseResult.Choices.FirstOrDefault().Text}");

            if (SettingsService.AIType != "OpenAI")
            {
                try
                {
                    // Azure OpenAI - Test the embedding model
                    string VectorEmbedding = await GetVectorEmbedding("This is a test for embedding", false);
                }
                catch (Exception ex)
                {
                    LogService.WriteToLog($"Azure OpenAI - Test the embedding model - Error: {ex.Message}");
                                        
                    throw new Exception("Error: You must set a proper Azure OpenAI embedding model");
                }
            }          

            return true;
        }
        #endregion
    }
}
