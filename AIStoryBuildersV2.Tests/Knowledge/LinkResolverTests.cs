using AIStoryBuildersV2.Shared.Knowledge;

namespace AIStoryBuildersV2.Tests;

public class LinkResolverTests
{
    static readonly string[] Pages =
    [
        "Welcome.md",
        "Getting Started/Install.md",
        "Getting Started/Linking Pages.md",
        "Other/Install.md",
        "Deep/Nested/Folder/Unique.md",
        "Projects/Alpha/Overview.md",
        "B/Tie.md",
        "A/Tie.md",
        "Long/Path/Short.md",
        "X/Short.md",
    ];

    static PageDirectory Dir() => new(Pages,
    [
        ("Projects/Alpha/Overview.md", "Alpha", ["Project A", "PA"]),
        ("Welcome.md", "Welcome", []),
    ]);

    static Resolution? Resolve(string source, string target) => LinkResolver.ResolveWiki(Dir(), source, target);

    [Theory]
    [InlineData("Welcome.md", "Getting Started/Install", "Getting Started/Install.md")]
    [InlineData("Other/Install.md", "Getting Started/Install", "Getting Started/Install.md")]
    [InlineData("Welcome.md", "getting started/INSTALL", "Getting Started/Install.md")]
    [InlineData("Welcome.md", "Getting Started/Install.md", "Getting Started/Install.md")]
    [InlineData("Welcome.md", "/Getting Started/Install", "Getting Started/Install.md")]
    public void Step1_PathsAreRootRelative(string source, string target, string expected)
    {
        var r = Resolve(source, target);
        Assert.Equal(expected, r?.Path);
        Assert.Equal(ResolveStep.Path, r?.Step);
    }

    [Fact]
    public void Step1_MissingPath_DoesNotFallBackToTheFileName()
    {
        Assert.Null(Resolve("Welcome.md", "Nowhere/Install"));
    }

    [Theory]
    [InlineData("Getting Started/Linking Pages.md", "Getting Started/Install.md")]
    [InlineData("Other/Install.md", "Other/Install.md")]
    public void Step2_SameFolderWins(string source, string expected)
    {
        var r = Resolve(source, "Install");
        Assert.Equal(expected, r?.Path);
        Assert.Equal(ResolveStep.SameFolder, r?.Step);
    }

    [Fact]
    public void Step2_AtTheRoot()
    {
        var r = Resolve("Getting Started/Install.md", "welcome");
        Assert.Equal("Welcome.md", r?.Path);
        Assert.Equal(ResolveStep.Anywhere, r?.Step);
        Assert.Equal(ResolveStep.SameFolder, Resolve("Other.md", "Welcome")?.Step);
    }

    [Fact]
    public void Step3_AnywhereByFileName()
    {
        var r = Resolve("Welcome.md", "Unique");
        Assert.Equal("Deep/Nested/Folder/Unique.md", r?.Path);
        Assert.Equal(ResolveStep.Anywhere, r?.Step);
    }

    [Fact]
    public void Step3_TiesGoToTheShortestPath_ThenOrdinalOrder()
    {
        Assert.Equal("X/Short.md", Resolve("Welcome.md", "Short")?.Path);
        Assert.Equal("A/Tie.md", Resolve("Welcome.md", "Tie")?.Path);
        Assert.Equal("Other/Install.md", Resolve("Welcome.md", "Install")?.Path);
    }

    [Theory]
    [InlineData("Alpha")]
    [InlineData("alpha")]
    [InlineData("Project A")]
    [InlineData("PA")]
    public void Step4_TitleOrAlias(string target)
    {
        var r = Resolve("Welcome.md", target);
        Assert.Equal("Projects/Alpha/Overview.md", r?.Path);
        Assert.Equal(ResolveStep.TitleOrAlias, r?.Step);
    }

    [Fact]
    public void Step4_IsSkippedWhenTitlesAreDisabled()
    {
        Assert.Null(LinkResolver.ResolveWiki(Dir(), "Welcome.md", "Alpha", useTitles: false));
    }

    [Fact]
    public void FileNameBeatsTitle()
    {
        var dir = new PageDirectory(["Alpha.md", "P/Overview.md"], [("P/Overview.md", "Alpha", [])]);
        Assert.Equal("Alpha.md", LinkResolver.ResolveWiki(dir, "x.md", "Alpha")?.Path);
    }

    [Theory]
    [InlineData("Roadmap")]
    [InlineData("Nowhere/Roadmap")]
    [InlineData("")]
    [InlineData("  ")]
    public void Step5_Unresolved(string target)
    {
        Assert.Null(Resolve("Welcome.md", target));
    }

    [Theory]
    [InlineData("Getting Started/Install.md", "Linking%20Pages.md", "Getting Started/Linking Pages.md")]
    [InlineData("Getting Started/Install.md", "Linking Pages.md", "Getting Started/Linking Pages.md")]
    [InlineData("Getting Started/Install.md", "../Welcome.md", "Welcome.md")]
    [InlineData("Getting Started/Install.md", "./../Welcome.md#top", "Welcome.md")]
    [InlineData("Welcome.md", "Getting%20Started/Install.md", "Getting Started/Install.md")]
    [InlineData("Other/Install.md", "/Welcome.md", "Welcome.md")]
    [InlineData("Other/Install.md", "..\\Welcome.md", "Welcome.md")]
    public void Markdown_ResolvesRelativeToTheSourceFolder(string source, string url, string expected)
    {
        Assert.Equal(expected, LinkResolver.ResolveMarkdown(Dir(), source, url));
    }

    [Theory]
    [InlineData("https://example.com/Welcome.md")]
    [InlineData("http://example.com")]
    [InlineData("mailto:a@b.c")]
    [InlineData("#Welcome")]
    [InlineData("../../Welcome.md")]
    [InlineData("Missing.md")]
    public void Markdown_IgnoresExternalAndInvalid(string url)
    {
        Assert.Null(LinkResolver.ResolveMarkdown(Dir(), "Other/Install.md", url));
    }

    [Theory]
    [InlineData("Welcome.md", "Getting Started/Install.md", "Getting%20Started/Install.md")]
    [InlineData("Getting Started/Install.md", "Welcome.md", "../Welcome.md")]
    [InlineData("Getting Started/Install.md", "Getting Started/Linking Pages.md", "Linking%20Pages.md")]
    [InlineData("A/B/c.md", "A/D/e.md", "../D/e.md")]
    [InlineData("A/B/c.md", "X/y.md", "../../X/y.md")]
    public void RelativeUrl_RoundTripsThroughResolve(string source, string target, string expected)
    {
        var url = LinkResolver.RelativeUrl(source, target);
        Assert.Equal(expected, url);
        Assert.Equal(target, LinkResolver.RelativeTarget(source, url));
    }
}
