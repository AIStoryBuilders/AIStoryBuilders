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
        #region public async Task<Message> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel)
        public async Task<Message> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";

            LogService.WriteToLog($"CreateNewChapters using {GPTModel} - Start");

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization),null,new HttpClient() { Timeout= TimeSpan.FromSeconds(520) });

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // Update System Message
            SystemMessage = CreateSystemMessageCreateNewChapters(JSONNewStory, ChapterCount);

            LogService.WriteToLog($"Prompt: {SystemMessage}");

            chatPrompts = new List<Message>();

            chatPrompts.Insert(0,
            new Message(
                Role.System,
                SystemMessage
                )
            );

            ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"Calling ChatGPT...", 70));

            // Get a response from ChatGPT 
            var FinalChatRequest = new ChatRequest(
                chatPrompts,
                model: GPTModel,
                temperature: 0.0,
                topP: 1,
                frequencyPenalty: 0,
                presencePenalty: 0,
                responseFormat: ChatResponseFormat.Json);

            // Check Moderation
            var ModerationResult = await api.ModerationsEndpoint.GetModerationAsync(SystemMessage);

            if (ModerationResult)
            {
                ModerationsResponse moderationsResponse = await api.ModerationsEndpoint.CreateModerationAsync(new ModerationsRequest(SystemMessage));

                // Serailize the ModerationsResponse
                string ModerationsResponseString = JsonConvert.SerializeObject(moderationsResponse.Results.FirstOrDefault().Categories);

                LogService.WriteToLog($"OpenAI Moderation flagged the content: [{SystemMessage}] as violating its policies: {ModerationsResponseString}");
                ReadTextEvent?.Invoke(this, new ReadTextEventArgs($"WARNING! OpenAI Moderation flagged the content as violating its policies. See the logs for more details.", 30));
            }

            ChatResponseResult = await api.ChatEndpoint.GetCompletionAsync(FinalChatRequest);

            // *****************************************************

            LogService.WriteToLog($"TotalTokens: {ChatResponseResult.Usage.TotalTokens} - ChatResponseResult - {ChatResponseResult.FirstChoice.Message.Content}");

            return ChatResponseResult.FirstChoice.Message;
        }
        #endregion

        // Methods

        #region private string CreateSystemMessageCreateNewChapters(string paramJSONNewStory, string paramChapterCount)
        private string CreateSystemMessageCreateNewChapters(string paramJSONNewStory, string paramChapterCount)
        {
            return "Given a story with the following structure: \n" +
                    "[ \n" +
                    $"{paramJSONNewStory} \n" +
                    "] \n" +
                    "Using only this information please: \n" +
                    $"#1 Create {paramChapterCount} chapters in a format like this: Chapter1, Chapter2, Chapter3. \n" +
                    "#2 A short chapter_synopsis description. Format this in story beats in a format like this: #Beat 1 - Something happens. #Beat 2 - The next things happens. #Beat 3 - Another thing happens. \n" +
                    "#3 A short 200 word first paragraph for each chapter. \n" +
                    "#4 A single timeline_name for each paragraph. \n" +
                    "#5 The list of character names that appear in each paragraph. \n" +
                    "Output JSON nothing else. \n" +
                    "Provide the results in the following JSON format: \n" +
                    "{ \n" +
                    "\"chapter\": [\n" +
                    "{ \n" +
                    "\"chapter_name\": chapter_name, \n" +
                    "\"chapter_synopsis\": chapter_synopsis, \n" +
                    "\"paragraphs\": [\n" +
                    "{ \n" +
                    "\"contents\": contents, \n" +
                    "\"location_name\": location_name, \n" +
                    "\"timeline_name\": timeline_name, \n" +
                    "\"character_names\": [character_names] \n" +
                    "} \n" +
                    "] \n" +
                    "} \n";
        }
        #endregion
    }
}
