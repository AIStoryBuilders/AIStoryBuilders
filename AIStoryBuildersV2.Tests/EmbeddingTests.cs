using System.Text.Json;
using AIStoryBuildersV2.Embeddings.Onnx;
using AIStoryBuildersV2.Shared.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>
/// T4: reference vectors from V1's model (Test-Data §8). <c>Fixtures/embeddings.json</c> is what the WASM self-test
/// compares against; set ASB_REGENERATE_FIXTURES=1 to rewrite it.
/// </summary>
public class EmbeddingTests
{
    static readonly LocalEmbeddingGenerator Generator = new();

    [Fact]
    public async Task Vectors_are_384_normalized_floats()
    {
        var result = await Generator.GenerateAsync(["Balt and his father held the ridge until dusk.", ""]);
        Assert.All(result, e =>
        {
            Assert.Equal(384, e.Vector.Length);
            Assert.InRange(MathF.Sqrt(e.Vector.ToArray().Sum(v => v * v)), 0.999f, 1.001f);
        });
    }

    [Fact]
    public async Task Similar_sentences_are_closer_than_unrelated_ones()
    {
        var v = (await Generator.GenerateAsync(["Ravens carry messages for Odin.", "Odin's ravens bring him news.", "A cluster of turf houses by the water."]))
            .Select(e => e.Vector.ToArray()).ToList();
        Assert.True(EmbeddingSelfTest.Cosine(v[0], v[1]) > EmbeddingSelfTest.Cosine(v[0], v[2]));
    }

    [Fact]
    public async Task Committed_reference_vectors_match_the_model()
    {
        var path = FixtureFactory.SourceFixturePath("embeddings.json")!;
        var actual = (await Generator.GenerateAsync(EmbeddingSelfTest.Sentences)).Select(e => e.Vector.ToArray()).ToList();

        if (Environment.GetEnvironmentVariable("ASB_REGENERATE_FIXTURES") == "1" || !File.Exists(path))
        {
            var doc = EmbeddingSelfTest.Sentences.Select((s, i) => new EmbeddingSelfTest.Reference(s, actual[i])).ToList();
            File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false }));
        }

        var expected = EmbeddingSelfTest.ParseReferences(File.ReadAllText(path));
        Assert.Equal(EmbeddingSelfTest.Sentences, expected.Select(r => r.Text));
        for (var i = 0; i < expected.Count; i++)
            Assert.True(EmbeddingSelfTest.Cosine(expected[i].Vector, actual[i]) > 0.9999, $"sentence {i + 1}");
    }
}
