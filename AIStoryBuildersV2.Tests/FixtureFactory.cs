using System.Text;
using AIStoryBuildersV2.Shared.Stories;

namespace AIStoryBuildersV2.Tests;

/// <summary>
/// Generates the fixture story <c>Fixtures/V1Story/before/</c> (Test-Data §1) with V1's own byte conventions:
/// UTF-8 without BOM, CRLF after every line of WriteAllLines files, a blank line after each character and location
/// record, and no trailing newline in chapter and paragraph files. Dates use en-US short form.
/// </summary>
public static class FixtureFactory
{
    public const string Title = "Forgetting Baldur";
    const string CrLf = "\r\n";

    static string V(string text) => FixtureVectors.Format(text);

    /// <summary>Relative path → file bytes as text.</summary>
    public static IReadOnlyDictionary<string, string> BeforeFiles()
    {
        var s = Title + "/";
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["AIStoryBuildersStories.csv"] =
                "1|Forgetting Baldur|Fantasy|You are a software program that creates prose for novels.|Balt searches for the father who vanished after the battle at the ridge.|Ravens carry messages for Odin." + CrLf,
            [s + "Timelines.csv"] =
                "Years Before|The battle at the ridge and its aftermath.|11/1/2026 6:00 AM|11/2/2026 6:00 AM" + CrLf +
                "Present Day|Balt's search begins.|11/3/2026 6:00 AM|11/4/2026 6:00 AM" + CrLf,
            [s + "Characters/Balt.csv"] = Records(
                Bg("Appearance", "", "Tall, with a scar across his left cheek."),
                Bg("History", "Years Before", "Fought beside his father at the ridge.")),
            [s + "Characters/Freya.csv"] = Records(Bg("Appearance", "", "Red hair, always carries a bow.")),
            [s + "Characters/Father.csv"] = Records(Bg("History", "Years Before", "Vanished after the battle at the ridge.")),
            [s + "Locations/Ridge.csv"] = Records(Loc("A windswept ridge above the fjord.", "")),
            [s + "Locations/Village.csv"] = Records(Loc("A cluster of turf houses by the water.", "Present Day")),
            [s + "Chapters/Chapter1/Chapter1.txt"] = Syn("The battle at the ridge, and Father's disappearance."),
            [s + "Chapters/Chapter1/Paragraph1.txt"] = Par("Ridge", "Years Before", "[Balt,Father]", "Balt and his father held the ridge until dusk."),
            [s + "Chapters/Chapter1/Paragraph2.txt"] = Par("Ridge", "Years Before", "[Balt]", "When the smoke cleared, Father was gone."),
            [s + "Chapters/Chapter1/Paragraph3.txt"] = Par("Village", "Present Day", "[Balt,Freya]", "Years later, Freya found Balt mending nets."),
            [s + "Chapters/Chapter2/Chapter2.txt"] = Syn("Balt and Freya set out to learn the truth."),
            [s + "Chapters/Chapter2/Paragraph1.txt"] = Par("Village", "Present Day", "[Balt,Freya]", "Freya handed Balt a raven feather."),
            [s + "Chapters/Chapter2/Paragraph2.txt"] = Par("Ridge", "Present Day", "[Freya]", "At the ridge, Freya found a silver necklace in the heather."),
        };
        return files;
    }

    /// <summary>Writes <c>before/</c> under <paramref name="root"/> (a stand-in for Documents\AIStoryBuilders).</summary>
    public static void WriteBefore(string root)
    {
        foreach (var (relative, text) in BeforeFiles())
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, StoryFormat.Utf8NoBom);
        }
    }

    // V1's UI writers end each record with Environment.NewLine and WriteAllLines adds another CRLF.
    static string Records(params string[] records) => string.Concat(records.Select(r => r + CrLf + CrLf));

    static string Bg(string type, string timeline, string description) => $"{type}|{timeline}|{description}|{V(description)}";

    static string Loc(string description, string timeline) => $"{description}|{timeline}|{V(description)}";

    static string Syn(string synopsis) => $"{synopsis}|{V(synopsis)}";

    static string Par(string location, string timeline, string characters, string content) =>
        $"{location}|{timeline}|{characters}|{content}|{V(content)}";

    /// <summary>The path of the committed fixture folder in the source tree, or null when it can't be found.</summary>
    public static string? SourceFixturePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AIStoryBuildersV2.Tests.csproj"))) dir = dir.Parent;
        return dir is null ? null : Path.Combine(dir.FullName, "Fixtures", name);
    }

    public static Encoding Encoding => StoryFormat.Utf8NoBom;
}
