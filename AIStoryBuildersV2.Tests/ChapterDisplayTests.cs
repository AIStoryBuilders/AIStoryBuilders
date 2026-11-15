using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories;

namespace AIStoryBuildersV2.Tests;

/// <summary>UI-Behavior-Spec §14.1: the chapter title shown in the Manuscript Editor.</summary>
public class ChapterDisplayTests
{
    static Chapter Chapter(string? synopsis) => new() { ChapterName = "Chapter 1", Synopsis = synopsis };

    [Theory]
    [InlineData("The battle at the ridge, and Father's disappearance.", "The battle at the ridge, and Father's disappearance.")]
    [InlineData("#Beat 1 - Balt flees. #Beat 2 - Freya waits.", "Balt flees.")]
    [InlineData("#Beat 1 - A battle ends before midday.#Beat 2 - Balt searches.", "A battle ends before midday.")]
    [InlineData("#beat 3: no full stop #Beat 4 - more", "no full stop")]
    [InlineData("", "Chapter 1")]
    [InlineData("#Beat 1 - ", "Chapter 1")]
    public void Title_uses_the_first_sentence_without_beat_markers(string synopsis, string expected) =>
        Assert.Equal(expected, ChapterDisplay.Title(Chapter(synopsis)));

    [Fact]
    public void Long_titles_are_cut_to_60_characters()
    {
        var title = ChapterDisplay.Title(Chapter(new string('a', 80) + "."));
        Assert.Equal(new string('a', 60) + "…", title);
    }

    [Fact]
    public void Dropdown_text_is_the_name_alone_without_a_synopsis()
    {
        Assert.Equal("Chapter 1", ChapterDisplay.DropdownText(Chapter(null)));
        Assert.Equal("Chapter 1: Balt flees.", ChapterDisplay.DropdownText(Chapter("#Beat 1 - Balt flees.")));
    }
}
