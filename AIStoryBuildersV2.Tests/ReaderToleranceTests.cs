using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;

namespace AIStoryBuildersV2.Tests;

/// <summary>Test-Data §4: V2 readers skip bad records one at a time and handle V1's quirks.</summary>
public class ReaderToleranceTests : IDisposable
{
    readonly CultureScope _culture = new("en-US");
    readonly ServiceFixture _f = new();

    public void Dispose()
    {
        _f.Dispose();
        _culture.Dispose();
    }

    string Ch1(string file) => Path.Combine(_f.StoryDir, "Chapters", "Chapter1", file);

    [Fact]
    public void Crlf_inside_content_is_read_as_lf_with_the_vector_from_the_last_field()
    {
        File.WriteAllText(Ch1("Paragraph1.txt"), "Ridge|Present Day|[Freya]|Line one.\r\nLine two.|" + FixtureVectors.Format("x"));
        var p = _f.Service.GetParagraphs(_f.Chapter(1))[0];
        Assert.Equal("Line one.\nLine two.", p.ParagraphContent);
        Assert.Single(_f.Service.GetAllParagraphVectorsForChapter(_f.Chapter(1)), e => e.Id == "Ch1_P1");
    }

    [Fact]
    public void Manuscript_import_paragraph_keeps_blank_lines_and_unknown()
    {
        File.WriteAllText(Ch1("Paragraph1.txt"), "Unknown|Unknown|[]|A.\n\nB.|" + FixtureVectors.Format("x"));
        var p = _f.Service.GetParagraphs(_f.Chapter(1))[0];
        Assert.Equal("A.\n\nB.", p.ParagraphContent);
        Assert.Equal("Unknown", p.Location.LocationName);
        Assert.Equal("Unknown", p.Timeline.TimelineName);
        Assert.Empty(p.Characters);
    }

    [Theory]
    [InlineData("]", new string[0])]
    [InlineData("[]", new string[0])]
    [InlineData("[Balt, Freya,]", new[] { "Balt", "Freya" })]
    public void Character_lists(string field, string[] expected) =>
        Assert.Equal(expected, StoryFormat.ParseCharacterList(field));

    [Fact]
    public void Timeline_in_add_timeline_g_form_parses_with_null_stop()
    {
        File.WriteAllText(Path.Combine(_f.StoryDir, "Timelines.csv"), "X|d|11/5/2026 3:04:05 PM|\r\n");
        var t = Assert.Single(_f.Service.GetTimelines(_f.Story));
        Assert.Equal(new DateTime(2026, 11, 5, 15, 4, 5), t.StartDate);
        Assert.Null(t.StopDate);
    }

    [Fact]
    public void Narrow_no_break_space_before_pm_parses()
    {
        Assert.True(StoryFormat.TryParseDate("11/5/2026 3:04 PM", out var d));
        Assert.Equal(new DateTime(2026, 11, 5, 15, 4, 0), d);
    }

    [Fact]
    public void An_unparseable_timeline_row_is_skipped_and_the_rest_load()
    {
        File.WriteAllText(Path.Combine(_f.StoryDir, "Timelines.csv"),
            "Bad|x|not a date|\r\nGood|y|11/3/2026 6:00 AM|11/4/2026 6:00 AM\r\n");
        var t = Assert.Single(_f.Service.GetTimelines(_f.Story));
        Assert.Equal("Good", t.TimelineName);
        Assert.Contains(_f.Log.Lines, l => l.Contains("GetTimelines: skipped"));
    }

    [Fact]
    public void A_location_line_without_a_pipe_is_skipped()
    {
        File.WriteAllText(Path.Combine(_f.StoryDir, "Locations", "Ridge.csv"), "No pipe here\r\nGood|Present Day|[1]\r\n");
        var locations = _f.Service.GetLocations(_f.Story);
        Assert.Equal(2, locations.Count);
        Assert.Equal("Good", locations[0].LocationDescription.Single().Description);
    }

    [Fact]
    public void Stories_row_with_five_fields_has_empty_world_facts()
    {
        var entry = Assert.Single(CsvStoryLibraryStore.Parse("1|T|S|Th|Syn\r\n"));
        Assert.Equal("", entry.WorldFacts);
        Assert.Equal("Syn", entry.Synopsis);
    }

    [Fact]
    public void Stories_line_ending_with_two_crs_is_stripped()
    {
        var entry = Assert.Single(CsvStoryLibraryStore.Parse("1|T|S|Th|Syn|Facts\r\r\n"));
        Assert.Equal("Facts", entry.WorldFacts);
    }

    [Fact]
    public void A_non_chapter_folder_is_ignored()
    {
        Directory.CreateDirectory(Path.Combine(_f.StoryDir, "Chapters", "Notes"));
        Assert.Equal(2, _f.Service.GetChapters(_f.Story).Count);
        Assert.Contains(_f.Log.Lines, l => l.Contains("ignored folder 'Notes'"));
    }

    [Fact]
    public void A_copy_of_a_paragraph_file_is_ignored()
    {
        File.Copy(Ch1("Paragraph1.txt"), Ch1("Paragraph1 - Copy.txt"));
        Assert.Equal(3, _f.Service.GetParagraphs(_f.Chapter(1)).Count);
    }

    [Fact]
    public void Manifest_config_in_the_story_folder_is_ignored_by_readers()
    {
        File.WriteAllText(Path.Combine(_f.StoryDir, "Manifest.config"), "{\"Version\":\"03.62.00\"}");
        Assert.Equal(2, _f.Service.GetChapters(_f.Story).Count);
        Assert.Equal(3, _f.Service.GetCharacters(_f.Story).Count);
    }

    [Fact]
    public void A_comma_decimal_vector_is_treated_as_missing()
    {
        var deVector = "[" + string.Join(",", FixtureVectors.Of("x").Select(v => v.ToString(System.Globalization.CultureInfo.GetCultureInfo("de-DE")))) + "]";
        Assert.Null(StoryFormat.ParseVector(deVector));
        File.WriteAllText(Ch1("Paragraph1.txt"), "Ridge|Years Before|[Balt]|Text.|" + deVector);
        Assert.DoesNotContain(_f.Service.GetAllParagraphVectorsForChapter(_f.Chapter(1)), e => e.Id == "Ch1_P1");
        Assert.Equal("Text.", _f.Service.GetParagraphs(_f.Chapter(1))[0].ParagraphContent);
    }

    [Fact]
    public void A_missing_chapter_file_keeps_the_chapter()
    {
        File.Delete(Ch1("Chapter1.txt"));
        var chapters = _f.Service.GetChapters(_f.Story);
        Assert.Equal(2, chapters.Count);
        Assert.Equal("", chapters[0].Synopsis);
    }
}
