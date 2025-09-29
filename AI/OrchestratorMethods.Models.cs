using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Files;
using OpenAI.FineTuning;
using OpenAI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        #region public async Task<List<AIStoryBuilderModel>> ListFineTunedModelsAsync()
        public async Task<List<AIStoryBuilderModel>> ListFineTunedModelsAsync()
        {
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;

            // Fetch the list of models using the OpenAI API
            var fetcher = new OpenAiModelFetcher(ApiKey);
            var models = await fetcher.GetModelsAsync();

            List<AIStoryBuilderModel> colAIStoryBuilderModel = new List<AIStoryBuilderModel>();

            // Get the Model alias names from the database
            DatabaseService.LoadDatabase();
            var colDatabase = DatabaseService.colAIStoryBuildersDatabase;

            // Iterate through the fetched models
            foreach (var model in models.Data)
            {
                // Filter out models owned by "openai" or "system"
                if (!model.OwnedBy.Contains("openai")
                && !model.OwnedBy.Contains("system")
                && !model.Id.Contains("-step-"))
                {
                    AIStoryBuilderModel objAIStoryBuilderModel = new AIStoryBuilderModel();

                    objAIStoryBuilderModel.ModelId = model.Id;

                    var ModelName = colDatabase.Where(x => x.Key == model.Id).FirstOrDefault().Value;

                    if (ModelName != null)
                    {
                        objAIStoryBuilderModel.ModelName = ModelName;
                    }
                    else
                    {
                        objAIStoryBuilderModel.ModelName = model.Id;
                    }

                    colAIStoryBuilderModel.Add(objAIStoryBuilderModel);
                }
            }

            return colAIStoryBuilderModel;
        }
        #endregion

        #region public async Task<List<AIStoryBuilderModel>> ListAllModelsAsync()
        public async Task<List<AIStoryBuilderModel>> ListAllModelsAsync()
        {
            List<AIStoryBuilderModel> colAIStoryBuilderModel = new List<AIStoryBuilderModel>();

            AIStoryBuilderModel objAIStoryBuilderModelGPT5Mini = new AIStoryBuilderModel();
            objAIStoryBuilderModelGPT5Mini.ModelId = "gpt-5-mini";
            objAIStoryBuilderModelGPT5Mini.ModelName = "gpt-5-mini";

            colAIStoryBuilderModel.Add(objAIStoryBuilderModelGPT5Mini);

            AIStoryBuilderModel objAIStoryBuilderModelGPT5 = new AIStoryBuilderModel();
            objAIStoryBuilderModelGPT5.ModelId = "gpt-5";
            objAIStoryBuilderModelGPT5.ModelName = "gpt-5";

            colAIStoryBuilderModel.Add(objAIStoryBuilderModelGPT5);

            AIStoryBuilderModel objAIStoryBuilderModelGPT4 = new AIStoryBuilderModel();
            objAIStoryBuilderModelGPT4.ModelId = "gpt-4o";
            objAIStoryBuilderModelGPT4.ModelName = "gpt-4o";

            colAIStoryBuilderModel.Add(objAIStoryBuilderModelGPT4);

            AIStoryBuilderModel objAIStoryBuilderModelGPT41 = new AIStoryBuilderModel();
            objAIStoryBuilderModelGPT41.ModelId = "GPT-4.1";
            objAIStoryBuilderModelGPT41.ModelName = "GPT-4.1";

            colAIStoryBuilderModel.Add(objAIStoryBuilderModelGPT41);

            AIStoryBuilderModel objAIStoryBuilderModelGPT3 = new AIStoryBuilderModel();
            objAIStoryBuilderModelGPT3.ModelId = "gpt-3.5-turbo";
            objAIStoryBuilderModelGPT3.ModelName = "GPT-3.5";

            colAIStoryBuilderModel.Add(objAIStoryBuilderModelGPT3);

            // Fetch the list of the FineTune models
            var models = await ListFineTunedModelsAsync();

            if (models.Count > 0)
            {
                colAIStoryBuilderModel.AddRange(models);
            }

            return colAIStoryBuilderModel;
        }
        #endregion

        #region public async Task UpdateModelNameAsync(AIStoryBuilderModel paramaModel)
        public async Task UpdateModelNameAsync(AIStoryBuilderModel paramaModel)
        {
            // Get the Model alias names from the database
            DatabaseService.LoadDatabase();
            var colDatabase = DatabaseService.colAIStoryBuildersDatabase;

            // Create a new collection to store the updated model names
            Dictionary<string, string> colUpdatedDatabase = new Dictionary<string, string>();

            bool ModelExists = false;

            // Iterate through the existing database
            foreach (var item in colDatabase)
            {
                // If the model ID matches the provided model ID
                if (item.Key == paramaModel.ModelId)
                {
                    // Update the model name
                    colUpdatedDatabase.Add(paramaModel.ModelId, paramaModel.ModelName);
                    ModelExists = true;
                }
                else
                {
                    // Add the existing model name to the collection
                    colUpdatedDatabase.Add(item.Key, item.Value);
                }
            }

            if (!ModelExists)
            {
                // Add the new model name to the collection
                colUpdatedDatabase.Add(paramaModel.ModelId, paramaModel.ModelName);
            }

            // Save the updated collection to the database
            await DatabaseService.SaveDatabase(colUpdatedDatabase);
        }
        #endregion

        #region public async Task DeleteFineTuneModelAsync(AIStoryBuilderModel paramaModel)
        public async Task DeleteFineTuneModelAsync(AIStoryBuilderModel paramaModel)
        {
            // pull config
            var apiKey = SettingsService.ApiKey;
            var organization = SettingsService.Organization;

            // Delete the model via REST
            using var http = new HttpClient
            {
                BaseAddress = new Uri("https://api.openai.com/")
            };

            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            if (!string.IsNullOrWhiteSpace(organization))
                http.DefaultRequestHeaders.Add("OpenAI-Organization", organization);

            var deleteResponse = await http.DeleteAsync($"v1/models/{paramaModel.ModelId}");
            deleteResponse.EnsureSuccessStatusCode();

            // Remove any alias in the database

            // Get the Model alias names from the database
            DatabaseService.LoadDatabase();
            var colDatabase = DatabaseService.colAIStoryBuildersDatabase;

            // Create a new collection to store the updated model names
            Dictionary<string, string> colUpdatedDatabase = new Dictionary<string, string>();

            // Iterate through the existing database
            foreach (var item in colDatabase)
            {
                // Add all but the deleted one to the updated collection
                if (item.Key != paramaModel.ModelId)
                {
                    // Update the model name
                    colUpdatedDatabase.Add(item.Key, item.Value);
                }
            }

            // Save the updated collection to the database
            await DatabaseService.SaveDatabase(colUpdatedDatabase);
        }
        #endregion
    }
}
