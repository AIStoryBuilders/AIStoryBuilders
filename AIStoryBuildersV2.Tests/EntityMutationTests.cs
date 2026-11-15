using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories;

namespace AIStoryBuildersV2.Tests;

/// <summary>T2c: timeline, location and character writers and their cascades, checked against V1-File-Format §3 and §6.</summary>
public class EntityMutationTests : IDisposable
{
    const string CrLf = "\r\n";
    readonly CultureScope _culture = new("en-US");
    readonly ServiceFixture _f = new();

    public void Dispose()
    {
        _f.Dispose();
        _culture.Dispose();
    }

    string Read(string relative) => File.ReadAllText(Path.Combine(_f.StoryDir, relative.Replace('/', Path.DirectorySeparatorChar)));
    string Before(string relative) => FixtureFactory.BeforeFiles()[FixtureFactory.Title + "/" + relative];
    static string V(string text) => FixtureVectors.Format(text);

    Timeline PresentDay()
    {
        var t = _f.Service.GetTimelines(_f.Story).Single(t => t.TimelineName == "Present Day");
        t.Story = _f.Story;
        return t;
    }

    [Fact]
    public async Task Rename_timeline_cascades_like_v1()
    {
        var now = PresentDay();
        now.TimelineName = "Now";
        await _f.Service.UpdateTimelineAndTimelineNameAsync(now, "Present Day");

        Assert.Equal(
            "Years Before|The battle at the ridge and its aftermath.|11/1/2026 6:00 AM|11/2/2026 6:00 AM" + CrLf +
            "Now|Balt's search begins.|11/3/2026 6:00 AM|11/4/2026 6:00 AM" + CrLf, Read("Timelines.csv"));

        // Matching paragraphs are rewritten with a trailing CRLF; the others are untouched.
        Assert.Equal($"Village|Now|[Balt,Freya]|Years later, Freya found Balt mending nets.|{V("Years later, Freya found Balt mending nets.")}" + CrLf,
            Read("Chapters/Chapter1/Paragraph3.txt"));
        Assert.Equal($"Ridge|Now|[Freya]|At the ridge, Freya found a silver necklace in the heather.|{V("At the ridge, Freya found a silver necklace in the heather.")}" + CrLf,
            Read("Chapters/Chapter2/Paragraph2.txt"));
        Assert.Equal(Before("Chapters/Chapter1/Paragraph1.txt"), Read("Chapters/Chapter1/Paragraph1.txt"));

        // Every location and character file is rewritten and re-embedded, keeping V1's blank lines.
        Assert.Equal($"A cluster of turf houses by the water.|Now|{V("A cluster of turf houses by the water.")}" + CrLf + CrLf, Read("Locations/Village.csv"));
        Assert.Equal(Before("Locations/Ridge.csv"), Read("Locations/Ridge.csv"));
        Assert.Equal(Before("Characters/Balt.csv"), Read("Characters/Balt.csv"));
        Assert.Equal([FixtureFactory.Title], _f.Persistence.Dirty);
    }

    [Fact]
    public async Task Delete_timeline_clears_references()
    {
        await _f.Service.DeleteTimelineAndTimelineNameAsync(PresentDay(), "Present Day");
        Assert.Single(_f.Service.GetTimelines(_f.Story));
        Assert.StartsWith("Village||[Balt,Freya]|", Read("Chapters/Chapter1/Paragraph3.txt"));
        Assert.StartsWith("A cluster of turf houses by the water.||", Read("Locations/Village.csv"));
    }

    [Fact]
    public void Update_timeline_keeps_the_original_name_and_moves_the_row_last()
    {
        var t = _f.Service.GetTimelines(_f.Story).First();
        t.Story = _f.Story;
        t.TimelineDescription = "Edited";
        _f.Service.UpdateTimeline(t, "Years Before");
        var lines = Read("Timelines.csv").Split(CrLf);
        Assert.Equal("Present Day|Balt's search begins.|11/3/2026 6:00 AM|11/4/2026 6:00 AM", lines[0]);
        Assert.Equal("Years Before|Edited|11/1/2026 6:00 AM|11/2/2026 6:00 AM", lines[1]);
    }

    [Fact]
    public void Add_timeline_cleans_free_text_and_appends()
    {
        _f.Service.AddTimeline(new Timeline { Story = _f.Story, TimelineName = "A|B", TimelineDescription = "Line one\r\nline two|x", StartDate = new DateTime(2026, 11, 9, 10, 15, 30) });
        Assert.EndsWith("AB|Line one line twox|11/9/2026 10:15 AM|" + CrLf, Read("Timelines.csv"));
        Assert.Equal(3, _f.Service.GetTimelines(_f.Story).Count);
    }

    [Fact]
    public async Task Rename_character_rewrites_every_paragraph_like_v1()
    {
        var freya = _f.Service.GetCharacters(_f.Story).Single(c => c.CharacterName == "Freya");
        freya.Story = _f.Story;
        freya.CharacterName = "Freyja";
        await _f.Service.AddUpdateCharacterAsync(freya, "Freya");
        await _f.Service.UpdateCharacterNameAsync(freya, "Freya");

        Assert.False(File.Exists(Path.Combine(_f.StoryDir, "Characters", "Freya.csv")));
        Assert.Equal(Before("Characters/Freya.csv"), Read("Characters/Freyja.csv"));

        const string prose = "Years later, Freyja found Balt mending nets.";
        Assert.Equal($"Village|Present Day|[Balt,Freyja]|{prose}|{V(prose)}" + CrLf, Read("Chapters/Chapter1/Paragraph3.txt"));
        // Unchanged paragraphs are still rewritten with a trailing CRLF, keeping their vector.
        Assert.Equal(Before("Chapters/Chapter1/Paragraph1.txt") + CrLf, Read("Chapters/Chapter1/Paragraph1.txt"));
    }

    [Fact]
    public void Delete_character_removes_it_from_lists()
    {
        var father = new Character { Story = _f.Story, CharacterName = "Father" };
        _f.Service.DeleteCharacter(father, "Father");
        Assert.False(File.Exists(Path.Combine(_f.StoryDir, "Characters", "Father.csv")));
        Assert.StartsWith("Ridge|Years Before|[Balt]|Balt and his father", Read("Chapters/Chapter1/Paragraph1.txt"));
    }

    [Fact]
    public async Task Character_file_cleans_fields()
    {
        var c = new Character
        {
            Story = _f.Story,
            CharacterName = "Odin",
            CharacterBackground = [new CharacterBackground { Type = "Facts", Description = "One-eyed|wise\nold", Timeline = new Timeline { TimelineName = "Years Before" } }],
        };
        await _f.Service.AddUpdateCharacterAsync(c, "Od?in");
        Assert.Equal($"Facts|Years Before|One-eyedwise old|{V("One-eyedwise old")}" + CrLf + CrLf, Read("Characters/Odin.csv"));
    }

    [Fact]
    public async Task Locations_add_rename_delete()
    {
        var harbor = new Location
        {
            Story = _f.Story,
            LocationName = "Harbor",
            LocationDescription = [new LocationDescription { Description = "Boats.", Timeline = null }],
        };
        await _f.Service.AddLocationAsync(harbor);
        Assert.Equal($"Boats.||{V("Boats.")}" + CrLf + CrLf, Read("Locations/Harbor.csv"));
        Assert.True(_f.Service.LocationExists(new Location { Story = _f.Story, LocationName = "HARBOR" }));

        var ridge = new Location { Story = _f.Story, LocationName = "High Ridge" };
        _f.Service.UpdateLocationName(ridge, "Ridge");
        Assert.True(File.Exists(Path.Combine(_f.StoryDir, "Locations", "High Ridge.csv")));
        Assert.StartsWith("High Ridge|Years Before|", Read("Chapters/Chapter1/Paragraph1.txt"));

        _f.Service.DeleteLocation(new Location { Story = _f.Story, LocationName = "High Ridge" });
        Assert.False(File.Exists(Path.Combine(_f.StoryDir, "Locations", "High Ridge.csv")));
        Assert.StartsWith("|Years Before|", Read("Chapters/Chapter1/Paragraph1.txt"));
    }
}

/// <summary>D30: vectors are invariant; dates use the current culture's short form and read back with fallbacks.</summary>
public class CultureTests : IDisposable
{
    readonly ServiceFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Under_de_DE_vectors_are_invariant_and_dates_use_de_DE()
    {
        using var _ = new CultureScope("de-DE");
        var start = new DateTime(2026, 11, 9, 10, 15, 0);
        _f.Service.AddTimeline(new Timeline { Story = _f.Story, TimelineName = "Storm", TimelineDescription = "d", StartDate = start });
        var text = File.ReadAllText(Path.Combine(_f.StoryDir, "Timelines.csv"));
        Assert.Contains("Storm|d|" + start.ToShortDateString() + " " + start.ToShortTimeString() + "|", text);
        Assert.Contains("09.11.2026", text);

        // The en-US rows of before/ still read under de-DE through the fallback.
        var timelines = _f.Service.GetTimelines(_f.Story);
        Assert.Equal(3, timelines.Count);
        Assert.Equal(new DateTime(2026, 11, 1, 6, 0, 0), timelines[0].StartDate);
        Assert.Equal(start, timelines.Single(t => t.TimelineName == "Storm").StartDate);

        await _f.Service.AddLocationAsync(new Location { Story = _f.Story, LocationName = "Fjord", LocationDescription = [new LocationDescription { Description = "Cold." }] });
        var vector = File.ReadAllText(Path.Combine(_f.StoryDir, "Locations", "Fjord.csv")).Split('|')[2].Trim();
        Assert.NotNull(StoryFormat.ParseVector(vector));
        Assert.Contains('.', vector);
    }
}
