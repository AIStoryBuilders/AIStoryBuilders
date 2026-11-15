using System.Text;
using System.Text.RegularExpressions;
using AIStoryBuildersV2.Shared.Stories;

namespace AIStoryBuildersV2.Tests;

/// <summary>
/// Compares a story tree written by V2 with V1's golden output (Test-Data §3, comparer rules):
/// every file byte for byte, except that <c>Graph/**</c> is ignored, a vector field is checked only for its format
/// (384 invariant-culture numbers), and AIStoryBuildersStories.csv is compared on the fixture row without its Id.
/// </summary>
public static partial class V1TreeComparer
{
    const string VectorPlaceholder = "[VEC]";

    /// <param name="expectedRoot">A stories root such as <c>after-rename-character/</c>.</param>
    /// <param name="actualRoot">The stories root V2 wrote.</param>
    /// <returns>One message per difference; empty when the trees match.</returns>
    public static List<string> Compare(string expectedRoot, string actualRoot, string title = FixtureFactory.Title)
    {
        var differences = new List<string>();

        var expectedStory = Path.Combine(expectedRoot, title);
        var actualStory = Path.Combine(actualRoot, title);
        var expectedFiles = Files(expectedStory);
        var actualFiles = Files(actualStory);

        foreach (var missing in expectedFiles.Keys.Except(actualFiles.Keys)) differences.Add($"missing: {missing}");
        foreach (var extra in actualFiles.Keys.Except(expectedFiles.Keys)) differences.Add($"unexpected: {extra}");
        foreach (var path in expectedFiles.Keys.Intersect(actualFiles.Keys))
        {
            var expected = Normalize(File.ReadAllBytes(expectedFiles[path]), path, differences, "expected");
            var actual = Normalize(File.ReadAllBytes(actualFiles[path]), path, differences, "actual");
            if (expected != actual) differences.Add($"differs: {path}{Environment.NewLine}{FirstDifference(expected, actual)}");
        }

        var expectedRow = FixtureRow(expectedRoot, title);
        var actualRow = FixtureRow(actualRoot, title);
        if (expectedRow != actualRow) differences.Add($"stories row differs: expected '{expectedRow}', actual '{actualRow}'");

        return differences;
    }

    static Dictionary<string, string> Files(string storyRoot) =>
        !Directory.Exists(storyRoot)
            ? []
            : Directory.GetFiles(storyRoot, "*", SearchOption.AllDirectories)
                .Select(f => (Relative: Path.GetRelativePath(storyRoot, f).Replace('\\', '/'), Full: f))
                .Where(f => !f.Relative.StartsWith("Graph/", StringComparison.Ordinal))
                .ToDictionary(f => f.Relative, f => f.Full, StringComparer.Ordinal);

    /// <summary>Replaces each valid vector with a placeholder; an invalid vector is reported.</summary>
    static string Normalize(byte[] bytes, string path, List<string> differences, string side)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) differences.Add($"{side} has a BOM: {path}");
        return VectorRegex().Replace(text, m =>
        {
            if (StoryFormat.ParseVector(m.Value) is null) differences.Add($"{side} has an invalid vector in {path}");
            return VectorPlaceholder;
        });
    }

    /// <summary>The fixture story's row without its Id field.</summary>
    static string? FixtureRow(string root, string title)
    {
        var path = Path.Combine(root, "AIStoryBuildersStories.csv");
        if (!File.Exists(path)) return null;
        return StoryFormat.SplitLinesLf(File.ReadAllText(path))
            .Select(l => l.Split('|'))
            .Where(f => f.Length > 1 && f[1] == title)
            .Select(f => string.Join("|", f.Skip(1)))
            .FirstOrDefault();
    }

    static string FirstDifference(string expected, string actual)
    {
        var i = 0;
        while (i < expected.Length && i < actual.Length && expected[i] == actual[i]) i++;
        static string Show(string s, int at) => s.Substring(Math.Max(0, at - 40), Math.Min(80, s.Length - Math.Max(0, at - 40)))
            .Replace("\r", "\\r").Replace("\n", "\\n");
        return $"  at {i}: expected …{Show(expected, i)}…{Environment.NewLine}  at {i}: actual   …{Show(actual, i)}…";
    }

    // A bracketed list of numbers long enough to be an embedding (not a character list such as [Balt,Freya]).
    [GeneratedRegex(@"\[[-+0-9.,eE]{200,}\]")]
    private static partial Regex VectorRegex();
}
