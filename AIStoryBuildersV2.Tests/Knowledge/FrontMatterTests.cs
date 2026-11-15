using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Tests;

public class FrontMatterTests
{
    public static TheoryData<string> Fixtures => new()
    {
        "---\ntitle: A\n---\n# Body\n",
        "---\r\ntitle: A\r\ntags: [x, y]\r\n---\r\n# Body\r\n",
        "---\ntitle: A\n---\n",
        "---\ntitle: A\n---\n\n\nBody with --- inside\n---\nmore",
        "---\n---\nbody",
        "---\na: 1\nb:\n  - x\n  - y\n---\n# Body",
        "# No front-matter\n",
        "",
        "--- not a fence\nbody",
        "Text\n---\ntitle: late\n---\n",
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SplitThenJoin_IsLossless(string markdown)
    {
        var (yaml, body) = FrontMatter.Split(markdown);
        Assert.Equal(markdown, FrontMatter.Join(yaml, body));
    }

    [Fact]
    public void Split_KeepsYamlVerbatim()
    {
        var (yaml, body) = FrontMatter.Split("---\ntitle:   Spaced  # comment\ntags: [a]\n---\n# Body\n");
        Assert.Equal("title:   Spaced  # comment\ntags: [a]", yaml);
        Assert.Equal("# Body\n", body);
    }

    [Fact]
    public void Split_WithoutFrontMatter_ReturnsNullYaml()
    {
        Assert.Equal((null, "# Body"), FrontMatter.Split("# Body"));
        Assert.Equal((null, "---\nunterminated"), FrontMatter.Split("---\nunterminated"));
    }

    [Fact]
    public void Split_EmptyFrontMatter()
    {
        var (yaml, body) = FrontMatter.Split("---\n---\nbody");
        Assert.Equal("", yaml);
        Assert.Equal("body", body);
    }

    [Fact]
    public void Join_WithoutYaml_ReturnsTheBody()
    {
        Assert.Equal("body", FrontMatter.Join(null, "body"));
        Assert.Equal("---\ntitle: A\n---\nbody", FrontMatter.Join("title: A", "body"));
    }

    [Fact]
    public void Parse_ReadsKnownFields()
    {
        var data = FrontMatter.Parse("""
            title: "  My Page  "
            tags: [one, two]
            aliases:
              - Alias One
              - Alias Two
            sources:
              - https://example.com/a
            updated: 2026-10-31
            other: ignored
            """, out var error);

        Assert.Null(error);
        Assert.Equal("My Page", data.Title);
        Assert.Equal(["one", "two"], data.Tags);
        Assert.Equal(["Alias One", "Alias Two"], data.Aliases);
        Assert.Equal(["https://example.com/a"], data.Sources);
        Assert.Equal(new DateOnly(2026, 10, 31), data.Updated);
    }

    [Fact]
    public void Parse_AcceptsCommaSeparatedStrings_AndSingularAlias()
    {
        var data = FrontMatter.Parse("tags: a, b ,c\nalias: Solo\nupdated: 2026-01-02T10:00:00Z", out var error);
        Assert.Null(error);
        Assert.Equal(["a", "b", "c"], data.Tags);
        Assert.Equal(["Solo"], data.Aliases);
        Assert.Equal(new DateOnly(2026, 1, 2), data.Updated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Empty_IsEmpty(string? yaml)
    {
        Assert.Same(FrontMatterData.Empty, FrontMatter.Parse(yaml, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Parse_InvalidYaml_ReportsAnError()
    {
        var data = FrontMatter.Parse("title: [unclosed\ntags: x", out var error);
        Assert.NotNull(error);
        Assert.StartsWith("Line ", error);
        Assert.Same(FrontMatterData.Empty, data);
    }

    [Fact]
    public void Parse_NonMapping_ReportsAnError()
    {
        FrontMatter.Parse("- just\n- a list", out var error);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_BadDate_IsIgnored()
    {
        Assert.Null(FrontMatter.Parse("updated: someday", out var error).Updated);
        Assert.Null(error);
    }
}

public class EditorSanitizerTests
{
    [Theory]
    [InlineData(@"See \[\[Page\]\] here", "See [[Page]] here")]
    [InlineData(@"\[\[Getting Started/Install\]\]", "[[Getting Started/Install]]")]
    [InlineData(@"\[\[my\_page\|the alias\]\]", "[[my_page|the alias]]")]
    [InlineData(@"\[\[Install\#API key\]\]", "[[Install#API key]]")]
    [InlineData(@"[\[Half\]]", "[[Half]]")]
    [InlineData("[[Already fine]]", "[[Already fine]]")]
    [InlineData(@"Two \[\[A\]\] and \[\[B\]\]", "Two [[A]] and [[B]]")]
    [InlineData(@"Not a link \[x\]", @"Not a link \[x\]")]
    [InlineData("No brackets at all", "No brackets at all")]
    [InlineData("", "")]
    public void UnescapeWikiLinks(string input, string expected)
    {
        Assert.Equal(expected, EditorSanitizer.UnescapeWikiLinks(input));
    }
}
