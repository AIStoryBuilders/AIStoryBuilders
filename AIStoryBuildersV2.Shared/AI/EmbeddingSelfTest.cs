using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.AI;

/// <summary>
/// Compares this host's embeddings with the reference vectors V1's model produces (plan §10, Test-Data §8).
/// Used by the DEBUG "Embedding self-test" button; a pass is a cosine of at least 0.98 per sentence.
/// </summary>
public static class EmbeddingSelfTest
{
    public const double PassAt = 0.98;

    public static readonly string[] Sentences =
    [
        "Balt and his father held the ridge until dusk.",
        "The silver necklace was forged from a fallen star.",
        "Ravens carry messages for Odin.",
        "A cluster of turf houses by the water.",
        "",
    ];

    public sealed record Reference(string Text, float[] Vector);

    public sealed record Result(string Text, double Cosine)
    {
        public bool Passed => Cosine >= PassAt;
    }

    public static List<Reference> ParseReferences(string json) =>
        JsonSerializer.Deserialize<List<Reference>>(json) ?? [];

    /// <summary>The reference vectors embedded in this assembly (DEBUG builds), or null.</summary>
    public static List<Reference>? LoadEmbeddedReferences()
    {
        using var stream = typeof(EmbeddingSelfTest).Assembly.GetManifestResourceStream("embeddings.json");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return ParseReferences(reader.ReadToEnd());
    }

    public static async Task<List<Result>> RunAsync(IEmbeddingGenerator<string, Embedding<float>> generator, IReadOnlyList<Reference> references, CancellationToken ct = default)
    {
        var vectors = await generator.GenerateAsync(references.Select(r => r.Text), cancellationToken: ct);
        return references.Select((r, i) => new Result(r.Text, Cosine(r.Vector, vectors[i].Vector.ToArray()))).ToList();
    }

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
