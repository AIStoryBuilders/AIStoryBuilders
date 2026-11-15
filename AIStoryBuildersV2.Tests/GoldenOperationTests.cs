using AIStoryBuildersV2.Shared.Models;

namespace AIStoryBuildersV2.Tests;

/// <summary>
/// Test-Data §3: V2 runs each operation on a fresh copy of <c>before/</c> and must match V1's golden output
/// <c>after-&lt;operation&gt;/</c>. The golden folders are made by a person in V1 (T2b); until then these tests skip.
/// </summary>
public class GoldenOperationTests : IDisposable
{
    readonly CultureScope _culture = new("en-US");
    readonly ServiceFixture _f = new();

    public void Dispose()
    {
        _f.Dispose();
        _culture.Dispose();
    }

    static string Golden(string operation)
    {
        var path = FixtureFactory.SourceFixturePath("V1Story/after-" + operation);
        Skip.If(path is null || !Directory.Exists(path), $"after-{operation}/ has not been made in V1 yet (T2b, needs a person).");
        return path!;
    }

    void AssertMatches(string golden)
    {
        var differences = V1TreeComparer.Compare(golden, _f.Dir.Path);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [SkippableFact]
    public async Task Rename_timeline()
    {
        var golden = Golden("rename-timeline");
        var t = _f.Service.GetTimelines(_f.Story).Single(x => x.TimelineName == "Present Day");
        t.Story = _f.Story;
        t.TimelineName = "Now";
        await _f.Service.UpdateTimelineAndTimelineNameAsync(t, "Present Day");
        AssertMatches(golden);
    }

    [SkippableFact]
    public async Task Rename_character()
    {
        var golden = Golden("rename-character");
        var freya = _f.Service.GetCharacters(_f.Story).Single(c => c.CharacterName == "Freya");
        freya.Story = _f.Story;
        freya.CharacterName = "Freyja";
        // V1's CharacterEdit saves the file under the old name first, then renames.
        await _f.Service.AddUpdateCharacterAsync(freya, "Freya");
        await _f.Service.UpdateCharacterNameAsync(freya, "Freya");
        AssertMatches(golden);
    }

    [SkippableFact]
    public void Add_paragraph()
    {
        var golden = Golden("add-paragraph");
        _f.Service.AddParagraph(_f.Chapter(1), new Paragraph
        {
            Sequence = 2,
            Location = new Location { LocationName = "Ridge" },
            Timeline = new Timeline { TimelineName = "Years Before" },
            Characters = [new Character { CharacterName = "Balt" }],
        });
        AssertMatches(golden);
    }

    [SkippableFact]
    public void Delete_chapter()
    {
        var golden = Golden("delete-chapter");
        var chapter = _f.Chapter(1);
        _f.Service.DeleteChapter(chapter);
        _f.Service.RestructureChapters(chapter, RestructureType.Delete);
        AssertMatches(golden);
    }
}
