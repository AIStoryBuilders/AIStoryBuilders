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

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<string> WriteParagraph(JSONMasterStory objJSONMasterStory, string paramAIPrompt)
        public async Task<string> WriteParagraph(JSONMasterStory objJSONMasterStory, string paramAIPrompt)
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;
            string SystemMessage = "";
            string GPTModel = "gpt-4-1106-preview";

            ChatMessages = new List<ChatMessage>();

            if (SettingsService.FastMode == true)
            {
                GPTModel = "gpt-3.5-turbo-1106";
            }

            LogService.WriteToLog($"Detect Characters using {GPTModel} - Start");

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization), null, new HttpClient() { Timeout = TimeSpan.FromSeconds(520) });

            // Create a colection of chatPrompts
            ChatResponse ChatResponseResult = new ChatResponse();
            List<Message> chatPrompts = new List<Message>();

            // *****************************************************
            dynamic Databasefile = AIStoryBuildersDatabaseObject;

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

        #region private string CreateWriteParagraph(JSONMasterStory paramJSONMasterStory, string paramAIPrompt)
        private string CreateWriteParagraph(JSONMasterStory paramJSONMasterStory, string paramAIPrompt)
        {
            string strPrompt = "You are a function that will produce JSON that contains the contents of a paragraph for a novel. \n";

            // Add existing paragraph contents if provided
            if (paramJSONMasterStory.CurrentParagraph.contents != "")
            {
                strPrompt = strPrompt +
                    "#### Rewrite the existing paragraph that follows: \n" +
                    paramJSONMasterStory.CurrentParagraph.contents + "\n";
            }

            // Add prompt instruction if provided
            if (paramAIPrompt != "")
            {
                strPrompt = strPrompt +
                    "#### Use the following instructions: \n" +
                    paramAIPrompt + "\n";
            }

            // Add StoryTitle if provided
            if (paramJSONMasterStory.StoryTitle != "")
            {
                strPrompt = strPrompt +
                    $"#### The story title is {paramJSONMasterStory.StoryTitle}. \n";
            }

            // Add StoryTitle if provided
            if (paramJSONMasterStory.StoryTitle != "")
            {
                strPrompt = strPrompt +
                    $"#### The story title is {paramJSONMasterStory.StoryTitle}. \n";
            }

            // Add StorySynopsis if provided
            if (paramJSONMasterStory.StorySynopsis != "")
            {
                strPrompt = strPrompt +
                    $"#### The story synopsis is {paramJSONMasterStory.StorySynopsis}. \n";
            }

            strPrompt = strPrompt +
                "Only use information provided. Do not use any information not provided.\n" +
                "Produce a paragraph that is 500 words maximum. \n";

            // Instruction on how to provide the results
            strPrompt = strPrompt + "Provide the results in the following JSON format: \n" +
                "{\n" +
                "\"paragraph_content\": \"[paragraph_content]\" \n" +
                "}";

            return strPrompt;
        }
        #endregion
    }
}
