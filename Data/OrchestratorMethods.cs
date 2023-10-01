using Newtonsoft.Json;
using OpenAI;
using static AIStoryBuilders.Pages.Memory;

namespace AIStoryBuilders.Model
{
    public partial class OrchestratorMethods
    {
        public event EventHandler<ReadTextEventArgs> ReadTextEvent;
        public SettingsService SettingsService { get; set; }
        public LogService LogService { get; set; }
        public string Summary { get; set; }
        dynamic AIStoryBuildersDatabaseObject { get; set; }

        public List<ChatMessage> ChatMessages = new List<ChatMessage>();

        public List<(string, float)> similarities = new List<(string, float)>();

        public Dictionary<string, string> AIStoryBuildersMemory = new Dictionary<string, string>();

        // Constructor
        public OrchestratorMethods(SettingsService _SettingsService, LogService _LogService)
        {
            SettingsService = _SettingsService;
            LogService = _LogService;
        }

        // Reading Text

        #region private async Task<string> ExecuteRead(string Filename, int paramStartWordIndex, int intChunkSize)
        private async Task<string> ExecuteRead(string Filename, int paramStartWordIndex, int intChunkSize)
        {
            // Read the Text from the file
            var ReadTextResult = await ReadText(Filename, paramStartWordIndex, intChunkSize);

            // *****************************************************
            dynamic ReadTextFromFileObject = JsonConvert.DeserializeObject(ReadTextResult);
            string ReadTextFromFileText = ReadTextFromFileObject.Text;
            int intCurrentWord = ReadTextFromFileObject.CurrentWord;
            int intTotalWords = ReadTextFromFileObject.TotalWords;

            // *****************************************************
            dynamic Databasefile = AIStoryBuildersDatabaseObject;

            string strCurrentTask = Databasefile.CurrentTask;
            int intLastWordRead = intCurrentWord;
            string strSummary = Databasefile.Summary ?? "";

            // If we are done reading the text, then summarize it
            if (intCurrentWord >= intTotalWords)
            {
                strCurrentTask = "Summarize";
            }

            // Prepare object to save to AIStoryBuildersDatabase.json
            AIStoryBuildersDatabaseObject = new
            {
                CurrentTask = strCurrentTask,
                LastWordRead = intLastWordRead,
                Summary = strSummary
            };

            return ReadTextFromFileText;
        }
        #endregion

        #region private async Task<string> ReadText(string FileDocumentPath, int startWordIndex, int intChunkSize)
        private async Task<string> ReadText(string FileDocumentPath, int startWordIndex, int intChunkSize)
        {
            // Read the text from the file
            string TextFileRaw = "";

            // Open the file to get existing content
            using (var streamReader = new StreamReader(FileDocumentPath))
            {
                TextFileRaw = await streamReader.ReadToEndAsync();
            }

            // Split the text into words
            string[] TextFileWords = TextFileRaw.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // Get the total number of words
            int TotalWords = TextFileWords.Length;

            // Get words starting at the startWordIndex
            string[] TextFileWordsChunk = TextFileWords.Skip(startWordIndex).Take(intChunkSize).ToArray();

            // Set the current word to the startWordIndex + intChunkSize
            int CurrentWord = startWordIndex + intChunkSize;

            if (CurrentWord >= TotalWords)
            {
                // Set the current word to the total words
                CurrentWord = TotalWords;
            }

            string ReadTextFromFileResponse = """
                        {
                         "Text": "{TextFileWordsChunk}",
                         "CurrentWord": {CurrentWord},
                         "TotalWords": {TotalWords},
                        }
                        """;

            ReadTextFromFileResponse = ReadTextFromFileResponse.Replace("{TextFileWordsChunk}", string.Join(" ", TextFileWordsChunk));
            ReadTextFromFileResponse = ReadTextFromFileResponse.Replace("{CurrentWord}", CurrentWord.ToString());
            ReadTextFromFileResponse = ReadTextFromFileResponse.Replace("{TotalWords}", TotalWords.ToString());

            return ReadTextFromFileResponse;
        }
        #endregion

        // Memory and Vectors

        #region private async Task CreateVectorEntry(string vectorcontent)
        private async Task CreateVectorEntry(string VectorContent)
        {
            // **** Call OpenAI and get embeddings for the memory text
            // Create an instance of the OpenAI client
            var api = new OpenAIClient(new OpenAIAuthentication(SettingsService.ApiKey, SettingsService.Organization));
            // Get the model details
            var model = await api.ModelsEndpoint.GetModelDetailsAsync("text-embedding-ada-002");
            // Get embeddings for the text
            var embeddings = await api.EmbeddingsEndpoint.CreateEmbeddingAsync(VectorContent, model);
            // Get embeddings as an array of floats
            var EmbeddingVectors = embeddings.Data[0].Embedding.Select(d => (float)d).ToArray();
            // Loop through the embeddings
            List<VectorData> AllVectors = new List<VectorData>();
            for (int i = 0; i < EmbeddingVectors.Length; i++)
            {
                var embeddingVector = new VectorData
                {
                    VectorValue = EmbeddingVectors[i]
                };
                AllVectors.Add(embeddingVector);
            }
            // Convert the floats to a single string
            var VectorsToSave = "[" + string.Join(",", AllVectors.Select(x => x.VectorValue)) + "]";

            // Write the memory to the .csv file
            var AIStoryBuildersMemoryPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersMemory.csv";
            using (var streamWriter = new StreamWriter(AIStoryBuildersMemoryPath, true))
            {
                streamWriter.WriteLine(VectorContent + "|" + VectorsToSave);
            }
        }
        #endregion

        #region public async Task<List<(string, float)>> SearchMemory(string SearchText, int intResultsToReturn)
        public async Task<List<(string, float)>> SearchMemory(string SearchText, int intResultsToReturn)
        {
            // Clear the memory
            AIStoryBuildersMemory = new Dictionary<string, string>();

            // Read the lines from the .csv file
            var AIStoryBuildersMemoryPath =
            $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/AIStoryBuildersMemory.csv";

            // Read the lines from the .csv file
            foreach (var line in System.IO.File.ReadAllLines(AIStoryBuildersMemoryPath))
            {
                var splitLine = line.Split('|');
                var KEY = splitLine[0];
                var VALUE = splitLine[1];

                AIStoryBuildersMemory.Add(KEY, VALUE);
            }

            // **** Call OpenAI and get embeddings for the memory text
            // Create an instance of the OpenAI client
            var api = new OpenAIClient(new OpenAIAuthentication(SettingsService.ApiKey, SettingsService.Organization));
            // Get the model details
            var model = await api.ModelsEndpoint.GetModelDetailsAsync("text-embedding-ada-002");
            // Get embeddings for the text
            var embeddings = await api.EmbeddingsEndpoint.CreateEmbeddingAsync(SearchText, model);
            // Get embeddings as an array of floats
            var EmbeddingVectors = embeddings.Data[0].Embedding.Select(d => (float)d).ToArray();

            // Reset the similarities list
            similarities = new List<(string, float)>();

            // Calculate the similarity between the prompt's
            // embedding and each existing embedding
            foreach (var embedding in AIStoryBuildersMemory)
            {
                if (embedding.Value != null)
                {
                    if (embedding.Value != "")
                    {
                        var ConvertEmbeddingToFloats = JsonConvert.DeserializeObject<List<float>>(embedding.Value);

                        var similarity =
                        CosineSimilarity(
                            EmbeddingVectors,
                        ConvertEmbeddingToFloats.ToArray());

                        similarities.Add((embedding.Key, similarity));
                    }
                }
            }

            // Sort the results by similarity in descending order
            similarities.Sort((a, b) => b.Item2.CompareTo(a.Item2));

            return similarities.Take(intResultsToReturn).ToList();
        } 
        #endregion

        // Utility Methods

        #region public static float CosineSimilarity(float[] vector1, float[] vector2)
        public static float CosineSimilarity(float[] vector1, float[] vector2)
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

        #region public class ReadTextEventArgs : EventArgs
        public class ReadTextEventArgs : EventArgs
        {
            public string Message { get; set; }

            public ReadTextEventArgs(string message)
            {
                Message = message;
            }
        }
        #endregion
    }
}