using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Tests;

public class MarkdownParserTests
{
    readonly MarkdownPageParser _parser = new();

    [Theory]
    [InlineData("Target", "Target", null, null)]
    [InlineData("Target.md", "Target", null, null)]
    [InlineData("Folder/Target", "Folder/Target", null, null)]
    [InlineData("Target#Heading", "Target", "Heading", null)]
    [InlineData("Target|Alias", "Target", null, "Alias")]
    [InlineData(" Target # Heading | Alias ", "Target", "Heading", "Alias")]
    [InlineData("Target#|Alias", "Target", null, "Alias")]
    public void TryParseInner(string inner, string target, string? heading, string? alias)
    {
        Assert.True(WikiLinkInlineParser.TryParseInner(inner, out var t, out var h, out var a));
        Assert.Equal(target, t);
        Assert.Equal(heading, h);
        Assert.Equal(alias, a);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#Heading")]
    [InlineData("|Alias")]
    [InlineData(".md")]
    public void TryParseInner_RequiresATarget(string inner)
    {
        Assert.False(WikiLinkInlineParser.TryParseInner(inner, out _, out _, out _));
    }

    [Fact]
    public void WikiLink_SpanCoversTheWholeLink()
    {
        const string text = "See [[Target#H|Alias]] here.";
        var link = Assert.Single(_parser.Parse("p.md", text).Links);

        Assert.Equal(LinkKind.Wiki, link.Kind);
        Assert.Equal("Target", link.RawTarget);
        Assert.Equal("H", link.Heading);
        Assert.Equal("Alias", link.Alias);
        Assert.Equal("[[Target#H|Alias]]", text.Substring(link.SpanStart, link.SpanLength));
        Assert.Equal(1, link.Line);
    }

    [Fact]
    public void Spans_AreRelativeToTheWholeText_IncludingFrontMatter()
    {
        const string text = "---\ntitle: T\ntags: [a]\n---\n# Heading\n\nFirst [[One]] and\nsecond [[Two|2]] and [md](Sub/Three.md).\n";
        var links = _parser.Parse("p.md", text).Links;

        Assert.Equal(3, links.Count);
        Assert.Equal("[[One]]", text.Substring(links[0].SpanStart, links[0].SpanLength));
        Assert.Equal(7, links[0].Line);
        Assert.Equal("[[Two|2]]", text.Substring(links[1].SpanStart, links[1].SpanLength));
        Assert.Equal(8, links[1].Line);

        var md = links[2];
        Assert.Equal(LinkKind.Markdown, md.Kind);
        Assert.Equal("Sub/Three.md", md.RawTarget);
        Assert.Equal("[md](Sub/Three.md)", text.Substring(md.SpanStart, md.SpanLength));
        Assert.Equal("Sub/Three.md", text.Substring(md.UrlStart, md.UrlLength));
    }

    [Fact]
    public void Spans_WorkWithCrLf()
    {
        const string text = "---\r\ntitle: T\r\n---\r\nLine one\r\nLine two [[Target]]\r\n";
        var link = Assert.Single(_parser.Parse("p.md", text).Links);
        Assert.Equal("[[Target]]", text.Substring(link.SpanStart, link.SpanLength));
        Assert.Equal(5, link.Line);
    }

    [Fact]
    public void Links_InCode_AreSkipped()
    {
        const string text = "Use `[[Inline]]` like this.\n\n```\n[[Fenced]]\n```\n\n    [[Indented]]\n\nReal [[Link]].";
        var link = Assert.Single(_parser.Parse("p.md", text).Links);
        Assert.Equal("Link", link.RawTarget);
    }

    [Fact]
    public void ExternalLinks_AndImages_AreNotPageLinks()
    {
        const string text = "[web](https://example.com) [mail](mailto:a@b.c) [anchor](#top) ![img](pic.png) <https://auto.link> [ftp](ftp://x/y) [page](other.md)";
        var link = Assert.Single(_parser.Parse("p.md", text).Links);
        Assert.Equal("other.md", link.RawTarget);
    }

    [Fact]
    public void MalformedWikiLinks_AreIgnored()
    {
        const string text = "[[unclosed and [[a\nb]] and [[]] and [[#only]] and [single] and [[ok]]";
        var link = Assert.Single(_parser.Parse("p.md", text).Links);
        Assert.Equal("ok", link.RawTarget);
    }

    [Fact]
    public void Title_ComesFromFrontMatter_OrTheFileName()
    {
        Assert.Equal("Fancy", _parser.Parse("Folder/plain.md", "---\ntitle: Fancy\n---\nx").Title);
        Assert.Equal("plain", _parser.Parse("Folder/plain.md", "x").Title);
    }

    [Fact]
    public void FrontMatterError_IsReported()
    {
        var page = _parser.Parse("p.md", "---\ntitle: [oops\n---\nbody");
        Assert.NotNull(page.FrontMatterError);
        Assert.Equal("p", page.Title);
    }

    [Fact]
    public void PlainText_DropsMarkupAndFrontMatter()
    {
        var page = _parser.Parse("p.md", "---\ntitle: Secret Title\n---\n# Heading\n\nSome **bold** text with [[Target|an alias]] and [[Plain]].");
        Assert.Equal("Heading Some bold text with an alias and Plain.", page.PlainText);
        Assert.DoesNotContain("Secret", page.PlainText);
    }

    [Fact]
    public void FrontMatter_IsParsedIntoThePage()
    {
        var page = _parser.Parse("p.md", "---\ntags: [a, b]\naliases: [P]\n---\n");
        Assert.Equal(["a", "b"], page.FrontMatter.Tags);
        Assert.Equal(["P"], page.FrontMatter.Aliases);
        Assert.Empty(page.Links);
    }
}
