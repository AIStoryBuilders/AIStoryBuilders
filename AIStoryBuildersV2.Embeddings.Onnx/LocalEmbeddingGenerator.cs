using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AIStoryBuildersV2.Embeddings.Onnx;

/// <summary>
/// V1's local embedding generator (AI/LocalEmbeddingGenerator.cs): all-MiniLM-L6-v2 ONNX with V1's simplified
/// lower-case WordPiece tokenizer, mean pooling and L2 normalization, 384 dimensions. The only change from V1 is where
/// the model is loaded from: the files ship next to this assembly instead of in the MAUI app package.
/// </summary>
public sealed class LocalEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int MaxTokens = 256;
    public const int EmbeddingDimensions = 384;

    readonly string _modelDirectory;
    readonly object _initLock = new();
    InferenceSession? _session;
    Dictionary<string, int> _vocab = [];
    int _clsTokenId, _sepTokenId, _unkTokenId;

    /// <param name="modelDirectory">The folder holding all-MiniLM-L6-v2.onnx and vocab.txt. Defaults to Model/ next to this assembly.</param>
    public LocalEmbeddingGenerator(string? modelDirectory = null)
    {
        _modelDirectory = modelDirectory ?? Path.Combine(AppContext.BaseDirectory, "Model");
    }

    public EmbeddingGeneratorMetadata Metadata => new(nameof(LocalEmbeddingGenerator), null, "all-MiniLM-L6-v2", EmbeddingDimensions);

    InferenceSession EnsureInitialized()
    {
        if (_session is { } ready) return ready;
        lock (_initLock)
        {
            if (_session is { } session) return session;

            var vocab = new Dictionary<string, int>();
            var lines = File.ReadAllLines(Path.Combine(_modelDirectory, "vocab.txt"));
            for (var i = 0; i < lines.Length; i++) vocab[lines[i].Trim()] = i;
            _vocab = vocab;
            _clsTokenId = vocab.GetValueOrDefault("[CLS]", 101);
            _sepTokenId = vocab.GetValueOrDefault("[SEP]", 102);
            _unkTokenId = vocab.GetValueOrDefault("[UNK]", 100);

            _session = new InferenceSession(Path.Combine(_modelDirectory, "all-MiniLM-L6-v2.onnx"));
            return _session;
        }
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var session = EnsureInitialized();
        var results = new List<Embedding<float>>();

        foreach (var text in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokens = WordPieceTokenize(text ?? "", MaxTokens - 2);
            var seqLen = tokens.Count + 2;
            var inputIds = new long[seqLen];
            var attentionMask = new long[seqLen];
            var tokenTypeIds = new long[seqLen];

            inputIds[0] = _clsTokenId;
            for (var i = 0; i < tokens.Count; i++) inputIds[i + 1] = tokens[i];
            inputIds[seqLen - 1] = _sepTokenId;
            Array.Fill(attentionMask, 1L);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, [1, seqLen])),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, [1, seqLen])),
                NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds, [1, seqLen])),
            };

            using var output = session.Run(inputs);
            var tokenEmbeddings = output.First().AsEnumerable<float>().ToArray();
            results.Add(new Embedding<float>(L2Normalize(MeanPool(tokenEmbeddings, seqLen, EmbeddingDimensions))));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    /// <summary>V1's tokenizer: lower-case, split on space/tab/CR/LF, greedy WordPiece; an unknown remainder becomes [UNK].</summary>
    List<int> WordPieceTokenize(string text, int maxTokens)
    {
        var tokens = new List<int>();
        foreach (var word in text.ToLowerInvariant().Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (tokens.Count >= maxTokens) break;
            var remaining = word;
            var isFirst = true;
            while (remaining.Length > 0 && tokens.Count < maxTokens)
            {
                string? best = null;
                var bestLen = 0;
                for (var end = remaining.Length; end > 0; end--)
                {
                    var candidate = isFirst ? remaining[..end] : "##" + remaining[..end];
                    if (_vocab.ContainsKey(candidate))
                    {
                        best = candidate;
                        bestLen = end;
                        break;
                    }
                }

                if (best is null)
                {
                    tokens.Add(_unkTokenId);
                    break;
                }

                tokens.Add(_vocab[best]);
                remaining = remaining[bestLen..];
                isFirst = false;
            }
        }
        return tokens;
    }

    static float[] MeanPool(float[] tokenEmbeddings, int seqLen, int dim)
    {
        var pooled = new float[dim];
        for (var t = 0; t < seqLen; t++)
            for (var d = 0; d < dim; d++)
                pooled[d] += tokenEmbeddings[t * dim + d];
        for (var d = 0; d < dim; d++) pooled[d] /= seqLen;
        return pooled;
    }

    static float[] L2Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        return norm == 0 ? vector : vector.Select(v => v / norm).ToArray();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => _session?.Dispose();
}
