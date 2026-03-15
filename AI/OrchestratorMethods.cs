using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using AIStoryBuilders.Services;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Files;
using OpenAI.FineTuning;
using OpenAI.Models;
using System.ClientModel;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace AIStoryBuilders.AI
{
    public partial class OrchestratorMethods
    {
        public event EventHandler<ReadTextEventArgs> ReadTextEvent;
        public SettingsService SettingsService { get; set; }
        public LogService LogService { get; set; }
        public DatabaseService DatabaseService { get; set; }
        public string Summary { get; set; }

        public List<(string, float)> similarities = new List<(string, float)>();

        public Dictionary<string, string> AIStoryBuildersMemory = new Dictionary<string, string>();

        private readonly LocalEmbeddingGenerator _embeddingGenerator;

        // Constructor
        public OrchestratorMethods(SettingsService _SettingsService, LogService _LogService, DatabaseService _DatabaseService, LocalEmbeddingGenerator embeddingGenerator)
        {
            SettingsService = _SettingsService;
            LogService = _LogService;
            DatabaseService = _DatabaseService;
            _embeddingGenerator = embeddingGenerator;
        }

        // Memory and Vectors

        #region public async Task<string> GetVectorEmbedding(string EmbeddingContent, bool Combine)
        public async Task<string> GetVectorEmbedding(string EmbeddingContent, bool Combine)
        {
            var embeddings = await _embeddingGenerator.GenerateAsync(new[] { EmbeddingContent });
            var embeddingVectors = embeddings[0].Vector.ToArray();
            var VectorsToSave = "[" + string.Join(",", embeddingVectors) + "]";

            if (Combine)
            {
                return EmbeddingContent + "|" + VectorsToSave;
            }
            else
            {
                return VectorsToSave;
            }
        }
        #endregion

        #region public async Task<string> GetVectorEmbeddingAsFloats(string EmbeddingContent)
        public async Task<float[]> GetVectorEmbeddingAsFloats(string EmbeddingContent)
        {
            var embeddings = await _embeddingGenerator.GenerateAsync(new[] { EmbeddingContent });
            return embeddings[0].Vector.ToArray();
        }
        #endregion

        #region public async Task<float[][]> GetBatchEmbeddings(string[] texts)
        public async Task<float[][]> GetBatchEmbeddings(string[] texts)
        {
            var embeddings = await _embeddingGenerator.GenerateAsync(texts);
            return embeddings.Select(e => e.Vector.ToArray()).ToArray();
        }
        #endregion

        // Utility Methods

        public IChatClient CreateOpenAIClient()
        {
            return CreateOpenAIClient(SettingsService.AIModel);
        }

        #region public IChatClient CreateOpenAIClient()
        public IChatClient CreateOpenAIClient(string paramAIModel)
        {
            string ApiKey = SettingsService.ApiKey;
            string AIModel = paramAIModel;

            switch (SettingsService.AIType)
            {
                case "OpenAI":
                {
                    var options = new OpenAIClientOptions
                    {
                        OrganizationId = SettingsService.Organization,
                        NetworkTimeout = TimeSpan.FromSeconds(520)
                    };
                    var openAiClient = new OpenAIClient(new ApiKeyCredential(ApiKey), options);
                    return openAiClient.GetChatClient(AIModel).AsIChatClient();
                }

                case "Azure OpenAI":
                {
                    var options = new AzureOpenAIClientOptions
                    {
                        NetworkTimeout = TimeSpan.FromSeconds(520)
                    };
                    var azureClient = new AzureOpenAIClient(
                               new Uri(SettingsService.Endpoint),
                               new AzureKeyCredential(ApiKey), options);
                    return azureClient.GetChatClient(AIModel).AsIChatClient();
                }

                case "Anthropic":
                    return new AnthropicChatClient(ApiKey, AIModel);

                case "Google AI":
                    return new GoogleAIChatClient(ApiKey, AIModel);

                default:
                    throw new NotSupportedException(
                        $"AI provider '{SettingsService.AIType}' is not supported.");
            }
        }
        #endregion

        #region public float CosineSimilarity(float[] vector1, float[] vector2)
        public float CosineSimilarity(float[] vector1, float[] vector2)
        {
            // Initialize variables for dot product and
            // magnitudes of the vectors
            float dotProduct = 0;
            float magnitude1 = 0;
            float magnitude2 = 0;

            // Iterate through the vectors and calculate
            // the dot product and magnitudes
            for (int i = 0; i < vector1?.Length; i++)
            {
                // Calculate dot product
                dotProduct += vector1[i] * vector2[i];

                // Calculate squared magnitude of vector1
                magnitude1 += vector1[i] * vector1[i];

                // Calculate squared magnitude of vector2
                magnitude2 += vector2[i] * vector2[i];
            }

            // Take the square root of the squared magnitudes
            // to obtain actual magnitudes
            magnitude1 = (float)Math.Sqrt(magnitude1);
            magnitude2 = (float)Math.Sqrt(magnitude2);

            // Calculate and return cosine similarity by dividing
            // dot product by the product of magnitudes
            return dotProduct / (magnitude1 * magnitude2);
        }
        #endregion

        #region private string CombineAndSortLists(string paramExistingList, string paramNewList)
        private string CombineAndSortLists(string paramExistingList, string paramNewList)
        {
            // Split the lists into an arrays
            string[] ExistingListArray = paramExistingList.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string[] NewListArray = paramNewList.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Combine the lists
            string[] CombinedListArray = ExistingListArray.Concat(NewListArray).ToArray();

            // Remove duplicates
            CombinedListArray = CombinedListArray.Distinct().ToArray();

            // Sort the array
            Array.Sort(CombinedListArray);

            // Combine the array into a string
            string CombinedList = string.Join("\n", CombinedListArray);

            return CombinedList;
        }
        #endregion

        #region public static string TrimToMaxWords(string input, int maxWords = 500)
        public static string TrimToMaxWords(string input, int maxWords = 500)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            string[] words = input.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length <= maxWords)
                return input;

            return string.Join(" ", words.Take(maxWords));
        }
        #endregion

        #region public bool IsValidFolderName(string folderName)
        public bool IsValidFolderName(string folderName)
        {
            string invalidChars = new string(Path.GetInvalidPathChars()) + new string(Path.GetInvalidFileNameChars());
            Regex containsABadCharacter = new Regex("[" + Regex.Escape(invalidChars) + "]");
            if (containsABadCharacter.IsMatch(folderName))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
        #endregion

        #region public static string TrimInnerSpaces(string input)
        public static string TrimInnerSpaces(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            return Regex.Replace(input, @"\s{2,}", " ");
        }
        #endregion

        #region public string SanitizeFileName(string input)
        private static readonly char[] InvalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
        private static readonly string[] ReservedNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

        public string SanitizeFileName(string input)
        {
            // Strip out invalid characters
            string sanitized = new string(input.Where(ch => !InvalidFileNameChars.Contains(ch)).ToArray());

            // Remove the | character
            sanitized = sanitized.Replace("|", "");

            return sanitized;
        }
        #endregion

        #region public string ExtractJson(string json)
        /// <summary>
        /// Extracts and repairs JSON from LLM response text.
        /// Delegates to JsonRepairUtility for deterministic repair.
        /// </summary>
        public static string ExtractJson(string json)
        {
            return JsonRepairUtility.ExtractAndRepair(json);
        }
        #endregion

        #region public static List<string> ParseStringToList(string input)
        public static List<string> ParseStringToList(string input)
        {
            // Remove the brackets and split the string by comma
            string[] items = Regex.Replace(input, @"[\[\]]", "").Split(',');

            // Convert the array to a List<string> and return
            return new List<string>(items);
        }
        #endregion

        #region public class ReadTextEventArgs : EventArgs
        public class ReadTextEventArgs : EventArgs
        {
            public string Message { get; set; }
            public int DisplayLength { get; set; }

            public ReadTextEventArgs(string message, int display_length)
            {
                Message = message;
                DisplayLength = display_length;
            }
        }
        #endregion
    }
}