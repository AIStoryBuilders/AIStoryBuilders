using AIStoryBuilders.Model;
using AIStoryBuilders.Models.JSON;
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

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Calling AI provider to test access...", 5));

            var ChatResponseResult = await api.GetResponseAsync(SystemMessage);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokenCount} - ChatResponseResult - {ChatResponseResult.Text}");

            // Test local embeddings
            try
            {
                ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Testing local embedding model...", 5));
                string embeddingTest = await GetVectorEmbedding("This is a test for local embedding", false);
                if (string.IsNullOrEmpty(embeddingTest))
                {
                    throw new Exception("Local embedding returned empty result");
                }
                LogService.WriteToLog($"Local embedding test passed ({embeddingTest.Length} chars)");
            }
            catch (Exception ex)
            {
                LogService.WriteToLog($"Local embedding test - Error: {ex.Message}");
                throw new Exception($"Error: Local embedding model failed: {ex.Message}");
            }

            return true;
        }
        #endregion
    }
}
