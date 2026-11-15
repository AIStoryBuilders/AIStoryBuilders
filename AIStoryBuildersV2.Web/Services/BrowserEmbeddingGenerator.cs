using Microsoft.Extensions.AI;
using Microsoft.JSInterop;

namespace AIStoryBuildersV2.Web.Services;

/// <summary>
/// Embeddings in the browser with the vendored onnxruntime-web and the quantized all-MiniLM-L6-v2 (D18), from Online.
/// The module loads lazily on the first embed (R12). Unlike Online, errors are not turned into zero vectors: a failed
/// embed throws, so a bad vector is never written to a story.
/// </summary>
public sealed class BrowserEmbeddingGenerator(IJSRuntime js) : IEmbeddingGenerator<string, Embedding<float>>
{
    readonly SemaphoreSlim _load = new(1, 1);
    IJSObjectReference? _module;

    public EmbeddingGeneratorMetadata Metadata => new(nameof(BrowserEmbeddingGenerator), null, "all-MiniLM-L6-v2 (quantized)", 384);

    async Task<IJSObjectReference> ModuleAsync()
    {
        if (_module is { } loaded) return loaded;
        await _load.WaitAsync();
        try
        {
            // Relative to the page's <base href> (D18). ?v=2 keeps it apart from V1 Online's file at the same URL on the same origin.
            return _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/embedding.js?v=2");
        }
        finally
        {
            _load.Release();
        }
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync();
        var texts = values.Select(v => v ?? "").ToArray();
        var vectors = await module.InvokeAsync<float[][]>("generateEmbeddings", cancellationToken, (object)texts);
        return new GeneratedEmbeddings<Embedding<float>>(vectors.Select(v => new Embedding<float>(v)));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
