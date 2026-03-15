using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AIStoryBuilders.AI;

/// <summary>
/// Local embedding generator using all-MiniLM-L6-v2 ONNX model.
/// Produces 384-dimensional vectors for semantic similarity search.
/// Implements IEmbeddingGenerator for compatibility with Microsoft.Extensions.AI.
/// </summary>
public class LocalEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private InferenceSession _session;
    private Dictionary<string, int> _vocab;
    private bool _initialized;
    private readonly object _initLock = new();
    private const int MaxTokens = 256;
    private const int EmbeddingDimensions = 384;

    // Special token IDs
    private int _clsTokenId;
    private int _sepTokenId;
    private int _padTokenId;
    private int _unkTokenId;

    public EmbeddingGeneratorMetadata Metadata =>
        new(nameof(LocalEmbeddingGenerator), null, "all-MiniLM-L6-v2", EmbeddingDimensions);

    public LocalEmbeddingGenerator()
    {
        // Defer initialization — MAUI FileSystem is not available during DI setup
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

            var dataDir = FileSystem.AppDataDirectory;
            var modelPath = Path.Combine(dataDir, "all-MiniLM-L6-v2.onnx");
            var vocabPath = Path.Combine(dataDir, "vocab.txt");

            CopyAssetIfMissing("all-MiniLM-L6-v2.onnx", modelPath);
            CopyAssetIfMissing("vocab.txt", vocabPath);

            _session = new InferenceSession(modelPath);
            _vocab = LoadVocabulary(vocabPath);

            _clsTokenId = _vocab.GetValueOrDefault("[CLS]", 101);
            _sepTokenId = _vocab.GetValueOrDefault("[SEP]", 102);
            _padTokenId = _vocab.GetValueOrDefault("[PAD]", 0);
            _unkTokenId = _vocab.GetValueOrDefault("[UNK]", 100);

            _initialized = true;
        }
    }

    private static void CopyAssetIfMissing(string assetName, string destPath)
    {
        if (File.Exists(destPath)) return;
        using var stream = FileSystem.OpenAppPackageFileAsync(assetName).Result;
        using var dest = File.Create(destPath);
        stream.CopyTo(dest);
    }

    private static Dictionary<string, int> LoadVocabulary(string vocabPath)
    {
        var vocab = new Dictionary<string, int>();
        var lines = File.ReadAllLines(vocabPath);
        for (int i = 0; i < lines.Length; i++)
        {
            vocab[lines[i].Trim()] = i;
        }
        return vocab;
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions options = null,
        CancellationToken ct = default)
    {
        EnsureInitialized();
        var results = new List<Embedding<float>>();

        foreach (var text in values)
        {
            // 1. Tokenize using WordPiece
            var tokens = WordPieceTokenize(text, MaxTokens - 2); // reserve for [CLS] and [SEP]

            // 2. Build input arrays with [CLS] ... [SEP] framing
            var inputIds = new long[tokens.Count + 2];
            var attentionMask = new long[tokens.Count + 2];
            var tokenTypeIds = new long[tokens.Count + 2];

            inputIds[0] = _clsTokenId;
            attentionMask[0] = 1;
            tokenTypeIds[0] = 0;

            for (int i = 0; i < tokens.Count; i++)
            {
                inputIds[i + 1] = tokens[i];
                attentionMask[i + 1] = 1;
                tokenTypeIds[i + 1] = 0;
            }

            inputIds[tokens.Count + 1] = _sepTokenId;
            attentionMask[tokens.Count + 1] = 1;
            tokenTypeIds[tokens.Count + 1] = 0;

            int seqLen = tokens.Count + 2;

            // 3. Create ONNX tensors
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, seqLen });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, seqLen });
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, seqLen });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            // 4. Run inference
            using var output = _session.Run(inputs);

            // 5. Mean-pool the token embeddings → single 384-d vector
            var tokenEmbeddings = output.First().AsEnumerable<float>().ToArray();
            var pooled = MeanPool(tokenEmbeddings, seqLen, EmbeddingDimensions);

            // 6. L2-normalize
            var normalized = L2Normalize(pooled);

            results.Add(new Embedding<float>(normalized));
        }

        return new GeneratedEmbeddings<Embedding<float>>(results);
    }

    private List<int> WordPieceTokenize(string text, int maxTokens)
    {
        var tokens = new List<int>();
        var words = text.ToLowerInvariant().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (tokens.Count >= maxTokens) break;

            var remaining = word;
            bool isFirst = true;

            while (remaining.Length > 0 && tokens.Count < maxTokens)
            {
                string bestMatch = null;
                int bestLen = 0;

                for (int end = remaining.Length; end > 0; end--)
                {
                    var candidate = isFirst ? remaining.Substring(0, end) : "##" + remaining.Substring(0, end);
                    if (_vocab.ContainsKey(candidate))
                    {
                        bestMatch = candidate;
                        bestLen = end;
                        break;
                    }
                }

                if (bestMatch != null)
                {
                    tokens.Add(_vocab[bestMatch]);
                    remaining = remaining.Substring(bestLen);
                    isFirst = false;
                }
                else
                {
                    // Unknown token — add [UNK] and move on
                    tokens.Add(_unkTokenId);
                    break;
                }
            }
        }

        return tokens;
    }

    private static float[] MeanPool(float[] tokenEmbeddings, int seqLen, int dim)
    {
        var pooled = new float[dim];
        for (int t = 0; t < seqLen; t++)
            for (int d = 0; d < dim; d++)
                pooled[d] += tokenEmbeddings[t * dim + d];
        for (int d = 0; d < dim; d++)
            pooled[d] /= seqLen;
        return pooled;
    }

    private static float[] L2Normalize(float[] vector)
    {
        var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
        return norm == 0 ? vector : vector.Select(v => v / norm).ToArray();
    }

    public object GetService(Type serviceType, object key = null)
        => serviceType == typeof(LocalEmbeddingGenerator) ? this : null;

    public void Dispose() => _session?.Dispose();
}
