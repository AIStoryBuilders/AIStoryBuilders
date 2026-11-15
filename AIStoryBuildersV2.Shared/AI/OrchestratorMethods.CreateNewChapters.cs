#nullable disable
using AIStoryBuildersV2.Shared.Models.JSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.AI;

public partial class OrchestratorMethods
{
    #region public async Task<ChatResponse> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel)
    public async Task<Microsoft.Extensions.AI.ChatResponse> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel)
    {
        LogService.WriteToLog($"CreateNewChapters using {GPTModel} - Start");

        IChatClient api = await GetChatClientAsync(GPTModel);

        // Build prompt messages
        var promptService = new PromptTemplateService();
        var messages = promptService.BuildMessages(
            PromptTemplateService.Templates.CreateNewChapters_System,
            PromptTemplateService.Templates.CreateNewChapters_User,
            new Dictionary<string, string>
            {
                ["StoryJSON"] = JSONNewStory,
                ["ChapterCount"] = ChapterCount
            });

        // V1 showed this for a fixed 70 seconds; it now closes as soon as the call ends, and 70 seconds is only the fallback.
        var progress = new ReadTextEventArgs("Calling AI provider...", 70);
        ReadTextEvent?.Invoke(this, progress);
        try
        {
            LogService.WriteToLog($"Prompt token estimate: {TokenEstimator.EstimateTokens(messages)}");

            var options = ChatOptionsFactory.CreateJsonOptions(await ProviderAsync(), GPTModel);

            var response = await api.GetResponseAsync(messages, options);

            LogService.WriteToLog($"TotalTokens: {response.Usage?.TotalTokenCount} - ChatResponseResult");

            return response;
        }
        finally
        {
            ReadTextEnded?.Invoke(this, progress);
        }
    }
    #endregion
}
