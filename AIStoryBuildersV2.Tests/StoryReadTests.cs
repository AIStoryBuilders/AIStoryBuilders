namespace AIStoryBuildersV2.Tests;

/// <summary>Test-Data §1.6: reading the V1 fixture story.</summary>
public class StoryReadTests : IDisposable
{
    readonly CultureScope _culture = new("en-US");
    readonly ServiceFixture _f = new();

    public void Dispose()
    {
        _f.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task GetStorys_reads_the_fixture_row()
    {
        var stories = await _f.Service.GetStorysAsync();
        var story = Assert.Single(stories);
        Assert.Equal("Forgetting Baldur", story.Title);
        Assert.Equal("Fantasy", story.Style);
        Assert.Equal("Ravens carry messages for Odin.", story.WorldFacts);
        Assert.Equal(1, story.Id);
    }

    [Fact]
    public void GetTimelines_sorts_by_start()
    {
        var timelines = _f.Service.GetTimelines(_f.Story);
        Assert.Equal(["Years Before", "Present Day"], timelines.Select(t => t.TimelineName));
        Assert.Equal(new DateTime(2026, 11, 1, 6, 0, 0), timelines[0].StartDate);
        Assert.Equal(new DateTime(2026, 11, 2, 6, 0, 0), timelines[0].StopDate);
    }

    [Fact]
    public void GetCharacters_reads_every_file()
    {
        var characters = _f.Service.GetCharacters(_f.Story).OrderBy(c => c.CharacterName).ToList();
        Assert.Equal(["Balt", "Father", "Freya"], characters.Select(c => c.CharacterName));
        Assert.Equal([2, 1, 1], characters.Select(c => c.CharacterBackground.Count));
        var history = characters[0].CharacterBackground[1];
        Assert.Equal("History", history.Type);
        Assert.Equal("Years Before", history.Timeline.TimelineName);
        Assert.Equal("Fought beside his father at the ridge.", history.Description);
        Assert.NotNull(Shared.Stories.StoryFormat.ParseVector(history.VectorContent));
    }

    [Fact]
    public void GetLocations_reads_descriptions_and_timelines()
    {
        var locations = _f.Service.GetLocations(_f.Story);
        Assert.Equal(["Ridge", "Village"], locations.Select(l => l.LocationName));
        Assert.Equal("", locations[0].LocationDescription.Single().Timeline.TimelineName);
        Assert.Equal("Present Day", locations[1].LocationDescription.Single().Timeline.TimelineName);
        Assert.Equal("A cluster of turf houses by the water.", locations[1].LocationDescription.Single().Description);
    }

    [Fact]
    public void GetChapters_reads_names_and_synopses()
    {
        var chapters = _f.Service.GetChapters(_f.Story);
        Assert.Equal(["Chapter 1", "Chapter 2"], chapters.Select(c => c.ChapterName));
        Assert.Equal([1, 2], chapters.Select(c => c.Sequence));
        Assert.Equal("The battle at the ridge, and Father's disappearance.", chapters[0].Synopsis);
        Assert.Equal("Balt and Freya set out to learn the truth.", chapters[1].Synopsis);
    }

    [Fact]
    public void GetParagraphs_reads_fields()
    {
        var paragraphs = _f.Service.GetParagraphs(_f.Chapter(1));
        Assert.Equal(3, paragraphs.Count);
        var p3 = paragraphs[2];
        Assert.Equal(3, p3.Sequence);
        Assert.Equal("Village", p3.Location.LocationName);
        Assert.Equal("Present Day", p3.Timeline.TimelineName);
        Assert.Equal(["Balt", "Freya"], p3.Characters.Select(c => c.CharacterName));
        Assert.Equal("Years later, Freya found Balt mending nets.", p3.ParagraphContent);
    }

    [Fact]
    public void Paragraph_vectors_are_read_for_rag()
    {
        var entries = _f.Service.GetAllParagraphVectorsForChapter(_f.Chapter(2));
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(384, e.Vectors.Length));
        Assert.Equal(FixtureVectors.Of("Freya handed Balt a raven feather."), entries[0].Vectors);
    }

    [Fact]
    public async Task LoadFullStory_loads_everything()
    {
        var story = await _f.Service.LoadFullStoryAsync(_f.Story);
        Assert.Equal("Fantasy", story.Style);
        Assert.Equal(3, story.Character.Count);
        Assert.Equal(2, story.Chapter.Count);
        Assert.Equal(2, story.Chapter[1].Paragraph.Count);
    }
}

/// <summary>The committed <c>Fixtures/V1Story/before/</c> must equal what <see cref="FixtureFactory"/> generates.</summary>
public class FixtureFileTests
{
    [Fact]
    public void Committed_before_fixture_matches_the_factory()
    {
        var root = FixtureFactory.SourceFixturePath("V1Story/before");
        Assert.NotNull(root);
        var expected = FixtureFactory.BeforeFiles();

        // Set ASB_REGENERATE_FIXTURES=1 to rewrite the committed copy.
        if (Environment.GetEnvironmentVariable("ASB_REGENERATE_FIXTURES") == "1")
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            FixtureFactory.WriteBefore(root!);
        }

        var actual = Directory.GetFiles(root!, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root!, f).Replace('\\', '/'), f => File.ReadAllBytes(f));
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (path, text) in expected)
            Assert.True(FixtureFactory.Encoding.GetBytes(text).AsSpan().SequenceEqual(actual[path]), path + " differs from the factory");
    }
}
