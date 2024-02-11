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

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> WriteParagraph(JSONMasterStory objJSONMasterStory, AIPrompt paramAIPrompt, string GPTModel)
        public async Task<string> WriteParagraph(JSONMasterStory objJSONMasterStory, AIPrompt paramAIPrompt, string GPTModel)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";

            LogService.WriteToLog($"Detect Characters using {GPTModel} - Start");

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization), null, new HttpClient() { Timeout = TimeSpan.FromSeconds(520) });

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // Update System Message
            SystemMessage = CreateWriteParagraph(objJSONMasterStory, paramAIPrompt);

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

            string strParagraphOutput = "";

            try
            {
                // Convert the JSON to a list of SimpleCharacters

                var JSONResult = ChatResponseResult.FirstChoice.Message.Content.ToString();

                dynamic data = JObject.Parse(JSONResult);

                strParagraphOutput = data.paragraph_content.ToString();
            }
            catch (Exception ex)
            {
                LogService.WriteToLog($"Error - WriteParagraph: {ex.Message} {ex.StackTrace ?? ""}");
            }

            return strParagraphOutput;
        }
        #endregion

        // Methods

        #region private string CreateWriteParagraph(JSONMasterStory paramJSONMasterStory, AIPrompt paramAIPrompt)
        private string CreateWriteParagraph(JSONMasterStory paramJSONMasterStory, AIPrompt paramAIPrompt)
        {
            string strPrompt = "You are a function that will produce JSON that contains the contents of a single paragraph for a novel. \n";

            // System Message if provided
            if (paramJSONMasterStory.SystemMessage != "")
            {
                strPrompt = strPrompt +
                    $"#### Please follow all these directions when creating the paragraph: {paramJSONMasterStory.SystemMessage.Trim()}. \n";
            }

            // Add StoryTitle if provided
            if (paramJSONMasterStory.StoryTitle != "")
            {
                strPrompt = strPrompt +
                    $"#### The story title is {paramJSONMasterStory.StoryTitle.Trim()}. \n";
            }

            // Add StoryStyle if provided
            if (paramJSONMasterStory.StoryStyle != "")
            {
                strPrompt = strPrompt +
                    $"#### The story style is {paramJSONMasterStory.StoryStyle.Trim()}. \n";
            }

            // Add StorySynopsis if provided
            if (paramJSONMasterStory.StorySynopsis != "")
            {
                strPrompt = strPrompt +
                    $"#### The story synopsis is {paramJSONMasterStory.StorySynopsis.Trim()}. \n";
            }

            // Add CurrentChapter if provided
            if (paramJSONMasterStory.CurrentChapter != null)
            {
                string ChapterSequence = paramJSONMasterStory.CurrentChapter.chapter_name.Split(' ')[1];

                strPrompt = strPrompt +
                    $"#### This is chapter number {ChapterSequence} in the story. \n";

                strPrompt = strPrompt +
                    $"#### This is the synopsis of chapter {ChapterSequence}: {paramJSONMasterStory.CurrentChapter.chapter_synopsis}. \n";

                if (paramJSONMasterStory.PreviousParagraphs != null)
                {
                    var JSONStringOfPreviousParagraphs = System.Text.Json.JsonSerializer.Serialize(paramJSONMasterStory.PreviousParagraphs);

                    strPrompt = strPrompt +
                        $"#### This is the JSON representation of the previous paragraphs in chapter {ChapterSequence}: {JSONStringOfPreviousParagraphs}. \n";
                }
            }

            // Add existing paragraph contents if provided
            if (paramJSONMasterStory.CurrentParagraph.contents != "")
            {
                strPrompt = strPrompt +
                    "#### This is the current contents of the next paragraph in the chapter: \n" +
                    paramJSONMasterStory.CurrentParagraph.contents + "\n";

                // Add CurrentLocation if provided
                if (paramJSONMasterStory.CurrentLocation != null)
                {
                    var JSONStringOfCurrentLocation = System.Text.Json.JsonSerializer.Serialize(paramJSONMasterStory.CurrentLocation);

                    strPrompt = strPrompt +
                        $"#### This is the JSON representation of the location description of the paragraph: {JSONStringOfCurrentLocation}. \n";
                }

                // Add CharacterList if provided
                if (paramJSONMasterStory.CharacterList != null)
                {
                    var JSONStringOfCharacterList = System.Text.Json.JsonSerializer.Serialize(paramJSONMasterStory.CharacterList);

                    strPrompt = strPrompt +
                        $"#### This is the JSON representation of the characters in the paragraph and their descriptions: {JSONStringOfCharacterList}. \n";
                }

                // Add RelatedParagraphs if provided
                if (paramJSONMasterStory.RelatedParagraphs != null)
                {
                    var JSONStringOfRelatedParagraphs = System.Text.Json.JsonSerializer.Serialize(paramJSONMasterStory.RelatedParagraphs);

                    strPrompt = strPrompt +
                        $"#### This is the JSON representation of related paragraphs that occur in previous chapters: {JSONStringOfRelatedParagraphs}. \n";
                }

                // Add prompt instruction if provided
                if (paramAIPrompt.AIPromptText.Trim() != "")
                {
                    strPrompt = strPrompt +
                        "#### Use the following instructions in re-writing the paragraph: \n" +
                        paramAIPrompt.AIPromptText.Trim() + "\n";
                }
                else
                {
                    strPrompt = strPrompt +
                        "#### Use the following instructions in writing the next paragraph in the chapter: \n" +
                        "Continue from the last paragraph. \n";
                }
            }
            else // No current Paragraph
            {
                // Add CurrentLocation if provided
                if (paramJSONMasterStory.CurrentLocation != null)
                {
                    var JSONStringOfCurrentLocation = System.Text.Json.JsonSerializer.Serialize(paramJSONMasterStory.CurrentLocation);

                    strPrompt = strPrompt +
                        $"#### This is the JSON representation of the location description of the paragraph: {JSONStringOfCurrentLocation}. \n";
                }

                // Add CharacterList if provided
                if (paramJSONMasterStory.CharacterList != null)
                {
                    var JSONStringOfCharacterList = System.Text.Json.JsonSerializer.Serialize(paramJSONMasterStory.CharacterList);

                    strPrompt = strPrompt +
                        $"#### This is the JSON representation of the characters in the paragraph and their descriptions: {JSONStringOfCharacterList}. \n";
                }

                // Add RelatedParagraphs if provided
                if (paramJSONMasterStory.RelatedParagraphs != null)
                {
                    var JSONStringOfRelatedParagraphs = System.Text.Json.JsonSerializer.Serialize(paramJSONMasterStory.RelatedParagraphs);

                    strPrompt = strPrompt +
                        $"#### This is the JSON representation of related paragraphs that occur in previous chapters: {JSONStringOfRelatedParagraphs}. \n";
                }

                // Add prompt instruction if provided
                if (paramAIPrompt.AIPromptText.Trim() != "")
                {
                    strPrompt = strPrompt +
                        "#### Use the following instructions in writing the next paragraph in the chapter: \n" +
                        paramAIPrompt.AIPromptText.Trim() + "\n";
                }
                else
                {
                    strPrompt = strPrompt +
                        "#### Use the following instructions in writing the next paragraph in the chapter: \n" +
                        "Write the next paragraph in the chapter. \n";
                }
            }

            strPrompt = strPrompt +
                "#### Only use information provided. Do not use any information not provided.\n" +
                "#### Write in the writing style of the provided content.\n" +
                "#### Insert a line break before a dialoge quote by a character the when they speak for the first time.\n" +
                $"#### Produce a single paragraph that is {paramAIPrompt.NumberOfWords} words maximum. \n";

            // Instruction on how to provide the results
            strPrompt = strPrompt + "#### Provide the results in the following JSON format: \n" +
                "{\n" +
                "\"paragraph_content\": \"[paragraph_content]\" \n" +
                "}";

            return strPrompt;
        }
        #endregion
    }
}
