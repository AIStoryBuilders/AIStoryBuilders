using AIStoryBuildersV2.Shared.Stories;

namespace AIStoryBuildersV2.Tests;

/// <summary>D29.</summary>
public class StoryTitleTests
{
    [Theory]
    [InlineData("Forgetting Baldur")]
    [InlineData("A")]
    [InlineData("Saltwater Saints; Part 2")]
    [InlineData("Con artist")]
    public void Valid_titles(string title) => Assert.Null(StoryTitle.Validate(title));

    [Theory]
    [InlineData(null, "Story Title is required")]
    [InlineData("", "Story Title is required")]
    [InlineData("a/b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a\\b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a:b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a*b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a?b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a\"b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a<b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a>b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a|b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a#b", "Story Title cannot contain / \\ : * ? \" < > | or #")]
    [InlineData("a..b", "Story Title cannot contain '..'.")]
    [InlineData(" a", "Story Title cannot start or end with a space or a dot.")]
    [InlineData("a ", "Story Title cannot start or end with a space or a dot.")]
    [InlineData(".a", "Story Title cannot start or end with a space or a dot.")]
    [InlineData("a.", "Story Title cannot start or end with a space or a dot.")]
    [InlineData("CON", "'CON' is a reserved name. Choose another title.")]
    [InlineData("nul", "'nul' is a reserved name. Choose another title.")]
    [InlineData("LPT1.txt", "'LPT1.txt' is a reserved name. Choose another title.")]
    public void Rejected_titles(string? title, string message) => Assert.Equal(message, StoryTitle.Validate(title));

    [Fact]
    public void Over_100_characters_is_rejected()
    {
        Assert.Null(StoryTitle.Validate(new string('a', 100)));
        Assert.Equal("Story Title must be 100 characters or fewer.", StoryTitle.Validate(new string('a', 101)));
    }

    [Fact]
    public void Uniqueness_ignores_case() => Assert.True(StoryTitle.SameTitle("Forgetting Baldur", "FORGETTING baldur"));

    [Theory]
    [InlineData("Old story.", true)]
    [InlineData("CON", true)]
    [InlineData("../x", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    public void Existing_v1_folders_are_grandfathered(string title, bool safe) =>
        Assert.Equal(safe, StoryTitle.IsSafeExistingName(title));

    [Fact]
    public void Routes_escape_the_title() =>
        Assert.Equal("story/Forgetting%20Baldur?tab=kb", StoryTitle.WorkspaceHref("Forgetting Baldur", "kb"));
}
