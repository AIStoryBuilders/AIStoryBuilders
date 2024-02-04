using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Files;
using OpenAI.FineTuning;
using OpenAI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
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

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization));

            // Fetch the list of models using the OpenAI API
            var models =
            await api.ModelsEndpoint.GetModelsAsync();

            List<AIStoryBuilderModel> colAIStoryBuilderModel = new List<AIStoryBuilderModel>();

            // Get the Model alias names from the database
            DatabaseService.LoadDatabase();
            var colDatabase = DatabaseService.colAIStoryBuildersDatabase;

            // Iterate through the fetched models
            foreach (var model in models)
            {
                // Filter out models owned by "openai" or "system"
                if (!model.OwnedBy.Contains("openai")
                && !model.OwnedBy.Contains("system"))
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

            // Add GPT3 and GPT4 to the list of models
            AIStoryBuilderModel objAIStoryBuilderModelGPT4 = new AIStoryBuilderModel();
            objAIStoryBuilderModelGPT4.ModelId = "gpt-4-turbo-preview";
            objAIStoryBuilderModelGPT4.ModelName = "GPT-4";

            colAIStoryBuilderModel.Add(objAIStoryBuilderModelGPT4);

            AIStoryBuilderModel objAIStoryBuilderModelGPT3 = new AIStoryBuilderModel();
            objAIStoryBuilderModelGPT3.ModelId = "gpt-3.5-turbo-1106";
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
            string Organization = SettingsService.Organization;
            string ApiKey = SettingsService.ApiKey;

            // Create a new OpenAIClient object
            // with the provided API key and organization
            var api = new OpenAIClient(new OpenAIAuthentication(ApiKey, Organization));

            await api.ModelsEndpoint.DeleteFineTuneModelAsync(paramaModel.ModelId);

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
