using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.AI;

/// <summary>Registered only when a host has no embedding generator yet; every call fails with a clear message.</summary>
public sealed class UnavailableEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Local embeddings are not available in this app.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
