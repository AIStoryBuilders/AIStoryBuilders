using AIStoryBuildersV2.Shared.Knowledge;
using System.Reflection;

namespace AIStoryBuildersV2.Tests;

/// <summary>KGB's seeder, kept for the ported tests only: writes the embedded Seed/*.md pages when the root is empty.</summary>
public static class KnowledgeSeeder
{
    const string Prefix = "Seed/";

    public static IReadOnlyList<(string Path, string Markdown)> GetSeedPages()
    {
        var assembly = typeof(KnowledgeSeeder).Assembly;
        var pages = new List<(string, string)>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            pages.Add((normalized[Prefix.Length..], reader.ReadToEnd().Replace("\r\n", "\n")));
        }
        pages.Sort((a, b) => StringComparer.Ordinal.Compare(a.Item1, b.Item1));
        return pages;
    }

    /// <summary>"Empty" means no .md pages and no visible folders.</summary>
    public static async Task<bool> SeedIfEmptyAsync(IKnowledgeStore store, CancellationToken ct = default)
    {
        var children = await store.GetChildrenAsync("", ct);
        if (children.Count > 0) return false;

        foreach (var (path, markdown) in GetSeedPages())
            await store.SavePageAsync(path, markdown, 0, ct);
        return true;
    }
}
