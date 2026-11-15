using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Models.JSON;
using AIStoryBuildersV2.Shared.Stories;
using Character = AIStoryBuildersV2.Shared.Models.Character;

namespace AIStoryBuildersV2.Tests;

/// <summary>T2d: story, chapter and paragraph writers and renumbering (V1-File-Format §3.1, §3.9, §3.10, §4.4).</summary>
public class StoryEditTests : IDisposable
{
    const string CrLf = "\r\n";
    readonly CultureScope _culture = new("en-US");
    readonly ServiceFixture _f = new();

    public void Dispose()
    {
        _f.Dispose();
        _culture.Dispose();
    }

    string Path_(string relative) => Path.Combine(_f.StoryDir, relative.Replace('/', Path.DirectorySeparatorChar));
    string Read(string relative) => File.ReadAllText(Path_(relative));
    string Before(string relative) => FixtureFactory.BeforeFiles()[FixtureFactory.Title + "/" + relative];
    static string V(string text) => FixtureVectors.Format(text);

    [Fact]
    public void Add_paragraph_writes_an_empty_record_and_shifts_the_rest()
    {
        _f.Service.AddParagraph(_f.Chapter(1), new Paragraph
        {
            Sequence = 2,
            Location = new Location { LocationName = "Ridge" },
            Timeline = new Timeline { TimelineName = "Years Before" },
            Characters = [new Character { CharacterName = "Balt" }],
        });
        Assert.Equal("Ridge|Years Before|[Balt]||", Read("Chapters/Chapter1/Paragraph2.txt"));
        Assert.Equal(Before("Chapters/Chapter1/Paragraph2.txt"), Read("Chapters/Chapter1/Paragraph3.txt"));
        Assert.Equal(Before("Chapters/Chapter1/Paragraph3.txt"), Read("Chapters/Chapter1/Paragraph4.txt"));
        Assert.Equal(Before("Chapters/Chapter1/Paragraph1.txt"), Read("Chapters/Chapter1/Paragraph1.txt"));
        Assert.Equal("", _f.Service.GetParagraphs(_f.Chapter(1))[1].ParagraphContent);
    }

    [Fact]
    public void Add_paragraph_with_no_characters_writes_empty_brackets()
    {
        _f.Service.AddParagraph(_f.Chapter(2), new Paragraph { Sequence = 3, Location = new Location(), Timeline = new Timeline(), Characters = [] });
        Assert.Equal("||[]||", Read("Chapters/Chapter2/Paragraph3.txt"));
    }

    [Fact]
    public async Task Update_paragraph_writes_crlf_and_no_trailing_newline()
    {
        await _f.Service.UpdateParagraph(_f.Chapter(2), new Paragraph
        {
            Sequence = 1,
            ParagraphContent = "Line one.\nLine two | three.",
            Location = new Location { LocationName = "Village" },
            Timeline = new Timeline { TimelineName = "Present Day" },
            Characters = [new Character { CharacterName = "Freya" }, new Character { CharacterName = " " }],
        });
        const string content = "Line one.\nLine two  three.";
        Assert.Equal($"Village|Present Day|[Freya]|Line one.\r\nLine two  three.|{V(content)}", Read("Chapters/Chapter2/Paragraph1.txt"));
        Assert.Equal(content, _f.Service.GetParagraphs(_f.Chapter(2))[0].ParagraphContent);
    }

    [Fact]
    public void Delete_paragraph_shifts_the_rest_down()
    {
        _f.Service.DeleteParagraph(_f.Chapter(1), new Paragraph { Sequence = 1 });
        Assert.Equal(Before("Chapters/Chapter1/Paragraph2.txt"), Read("Chapters/Chapter1/Paragraph1.txt"));
        Assert.Equal(Before("Chapters/Chapter1/Paragraph3.txt"), Read("Chapters/Chapter1/Paragraph2.txt"));
        Assert.False(File.Exists(Path_("Chapters/Chapter1/Paragraph3.txt")));
    }

    [Fact]
    public void Delete_chapter_then_restructure_renumbers_like_v1()
    {
        var chapter = _f.Chapter(1);
        _f.Service.DeleteChapter(chapter);
        _f.Service.RestructureChapters(chapter, RestructureType.Delete);
        Assert.Equal(["Chapter1"], Directory.GetDirectories(Path_("Chapters")).Select(Path.GetFileName));
        Assert.Equal(Before("Chapters/Chapter2/Chapter2.txt"), Read("Chapters/Chapter1/Chapter1.txt"));
        Assert.Equal(Before("Chapters/Chapter2/Paragraph2.txt"), Read("Chapters/Chapter1/Paragraph2.txt"));
        Assert.False(File.Exists(Path_("Chapters/Chapter1/Chapter2.txt")));
    }

    [Fact]
    public async Task Insert_chapter_before_makes_room_then_writes()
    {
        var first = _f.Chapter(1);
        _f.Service.RestructureChapters(first, RestructureType.Add);
        await _f.Service.InsertChapterAsync(new Chapter { ChapterName = "Chapter 1", Sequence = 1, Synopsis = "New Chapter", Story = _f.Story });

        var chapters = _f.Service.GetChapters(_f.Story);
        Assert.Equal(["New Chapter", "The battle at the ridge, and Father's disappearance.", "Balt and Freya set out to learn the truth."], chapters.Select(c => c.Synopsis));
        Assert.Equal($"New Chapter|{V("New Chapter")}", Read("Chapters/Chapter1/Chapter1.txt"));
        Assert.Equal(3, _f.Service.GetParagraphs(_f.Chapter(2)).Count);
    }

    [Fact]
    public async Task Add_and_update_chapter_clean_the_synopsis()
    {
        await _f.Service.AddChapterAsync(new Chapter { Synopsis = null, Story = _f.Story }, "Chapter3");
        Assert.Equal($" |{V(" ")}", Read("Chapters/Chapter3/Chapter3.txt"));

        await _f.Service.UpdateChapterAsync(new Chapter { ChapterName = "Chapter 3", Sequence = 3, Synopsis = "Two\r\nlines | here", Story = _f.Story });
        Assert.Equal($"Two lines  here|{V("Two lines  here")}", Read("Chapters/Chapter3/Chapter3.txt"));
    }

    [Fact]
    public async Task Update_story_cleans_and_moves_the_row_to_the_end()
    {
        await _f.Library.UpsertAsync(new StoryIndexEntry("0", "Other", "Drama", "T", "S", "", null));
        var story = (await _f.Service.GetStorysAsync()).First();
        story.Synopsis = "New|synopsis\nhere";
        story.WorldFacts = "Fact 1\r\nFact 2";
        await _f.Service.UpdateStoryAsync(story);

        var lines = File.ReadAllText(Path.Combine(_f.Dir.Path, "AIStoryBuildersStories.csv")).Split(CrLf);
        // V1 does not renumber the kept rows, so Ids repeat (V1-File-Format §3.1).
        Assert.StartsWith("2|Other|", lines[0]);
        Assert.Equal("2|Forgetting Baldur|Fantasy|You are a software program that creates prose for novels.|Newsynopsis here|Fact 1 Fact 2", lines[1]);
    }

    [Fact]
    public async Task Delete_story_removes_row_storage_and_folder()
    {
        await _f.Service.DeleteStoryAsync(FixtureFactory.Title);
        Assert.Empty(await _f.Service.GetStorysAsync());
        Assert.False(Directory.Exists(_f.StoryDir));
        Assert.Equal([FixtureFactory.Title], _f.Persistence.Deleted);
    }

    [Fact]
    public async Task Delete_story_removes_read_only_folders()
    {
        // OneDrive can mark story folders read-only; Directory.Delete then fails with access denied.
        var chapter = new DirectoryInfo(Path.Combine(_f.StoryDir, "Chapters", "Chapter1"));
        chapter.Attributes |= FileAttributes.ReadOnly;
        new DirectoryInfo(_f.StoryDir).Attributes |= FileAttributes.ReadOnly;

        await _f.Service.DeleteStoryAsync(FixtureFactory.Title);

        Assert.False(Directory.Exists(_f.StoryDir));
    }

    [Fact]
    public async Task Write_new_story_creates_v1_files()
    {
        var story = new Story { Title = "Saltwater Saints", Style = "Drama", Theme = "Theme", Synopsis = "Syn", WorldFacts = "" };
        var parsed = new JSONStory
        {
            characters = [new() { name = "Ada?", descriptions = [new() { description_type = "History", description = "Lost a|brother", timeline_name = "Storm" }] }],
            locations = [new() { name = "Light", descriptions = null }],
            timelines = [new() { name = "Storm", description = "Wind." }, new() { name = "Calm", description = "Quiet." }],
        };
        var chapters = new JSONChapters
        {
            chapter =
            [
                new() { chapter_synopsis = "The storm.", paragraphs = [new() { contents = "Ada climbed.", location_name = "Light, Shore", timeline_name = "Storm", character_names = [] }] },
                new() { chapter_synopsis = null, paragraphs = [] },
            ],
        };
        await _f.Service.WriteNewStoryAsync(story, parsed, chapters);

        var dir = Path.Combine(_f.Dir.Path, "Saltwater Saints");
        Assert.Equal($"History|Storm|Lost abrother|{V("Lost abrother")}" + CrLf + CrLf, File.ReadAllText(Path.Combine(dir, "Characters", "Ada.csv")));
        Assert.Equal($"Light||{V("Light")}" + CrLf + CrLf, File.ReadAllText(Path.Combine(dir, "Locations", "Light.csv")));
        Assert.Equal(2, _f.Service.GetTimelines(story).Count);
        Assert.Equal($"Light|Storm|[]|Ada climbed.|{V("Ada climbed.")}", File.ReadAllText(Path.Combine(dir, "Chapters", "Chapter1", "Paragraph1.txt")));
        // V2 always writes the chapter file, so V1 never sees a chapter folder without one.
        Assert.True(File.Exists(Path.Combine(dir, "Chapters", "Chapter2", "Chapter2.txt")));
        Assert.Contains(await _f.Service.GetStorysAsync(), s => s.Title == "Saltwater Saints");
        Assert.Equal(["Saltwater Saints"], _f.Persistence.Dirty);
    }
}
