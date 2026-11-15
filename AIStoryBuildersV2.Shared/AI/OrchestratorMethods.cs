#nullable disable
using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Settings;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.AI;

/// <summary>
/// V1's AI orchestration (AI/OrchestratorMethods*.cs). Every AI call gets its client from
/// <see cref="IChatClientProvider.GetClientAsync"/> for the model the caller chose (D9, D10); V1's SettingsService is gone (D31).
/// </summary>
public partial class OrchestratorMethods
{
    public event EventHandler<ReadTextEventArgs> ReadTextEvent;

    /// <summary>Raised with the same args as <see cref="ReadTextEvent"/> when that step is over, so its message can be closed.</summary>
    public event EventHandler<ReadTextEventArgs> ReadTextEnded;

    public LogService LogService { get; }

    readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    readonly IChatClientProvider _clients;
    readonly IAppSettingsService _settings;

    public OrchestratorMethods(LogService logService, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IChatClientProvider clients, IAppSettingsService settings)
    {
        LogService = logService;
        _embeddingGenerator = embeddingGenerator;
        _clients = clients;
        _settings = settings;
    }

    // Clients

    /// <summary>The client for <paramref name="model"/> (the default model when null), or an exception with the provider's reason.</summary>
    protected async Task<IChatClient> GetChatClientAsync(string model)
    {
        var result = await _clients.GetClientAsync(model);
        return result.Ok ? result.Client : throw new InvalidOperationException(result.Reason);
    }

    protected async Task<string> DefaultModelAsync() => (await _settings.GetAsync()).Model;

    protected async Task<LlmProvider> ProviderAsync() => (await _settings.GetAsync()).Provider;

    // Memory and Vectors

    /// <summary>
    /// V1's embedding helper. The vector is written with the invariant culture (D30).
    /// With <paramref name="Combine"/>, returns "{text}|{vector}"; callers pass text already cleaned for the file (D32).
    /// </summary>
    public async Task<string> GetVectorEmbedding(string EmbeddingContent, bool Combine)
    {
        var vector = await GetVectorEmbeddingAsFloats(EmbeddingContent);
        var vectorsToSave = StoryFormat.FormatVector(vector);
        return Combine ? EmbeddingContent + "|" + vectorsToSave : vectorsToSave;
    }

    public async Task<float[]> GetVectorEmbeddingAsFloats(string EmbeddingContent)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync([EmbeddingContent ?? ""]);
        return embeddings[0].Vector.ToArray();
    }

    public async Task<float[][]> GetBatchEmbeddings(string[] texts)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync(texts);
        return embeddings.Select(e => e.Vector.ToArray()).ToArray();
    }

    // Utility Methods

    public float CosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1 is null || vector2 is null || vector1.Length != vector2.Length) return 0;
        float dotProduct = 0, magnitude1 = 0, magnitude2 = 0;
        for (var i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }
        var denominator = MathF.Sqrt(magnitude1) * MathF.Sqrt(magnitude2);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }

    public static string TrimToMaxWords(string input, int maxWords = 500)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var words = input.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? input : string.Join(" ", words.Take(maxWords));
    }

    public static string TrimInnerSpaces(string input) =>
        string.IsNullOrEmpty(input) ? input : Regex.Replace(input, @"\s{2,}", " ");

    /// <summary>V1's SanitizeFileName (invalid file-name characters and '|' removed, not trimmed).</summary>
    public string SanitizeFileName(string input) => StoryFormat.SanitizeFileName(input);

    /// <summary>Extracts and repairs JSON from LLM response text (V1).</summary>
    public static string ExtractJson(string json) => JsonRepairUtility.ExtractAndRepair(json);

    /// <summary>A paragraph's character list. Unlike V1, names are trimmed and empty entries dropped (Test-Data §4).</summary>
    public static List<string> ParseStringToList(string input) => StoryFormat.ParseCharacterList(input);

    protected void RaiseReadText(string message, int displayLength) =>
        ReadTextEvent?.Invoke(this, new ReadTextEventArgs(message, displayLength));

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
}
