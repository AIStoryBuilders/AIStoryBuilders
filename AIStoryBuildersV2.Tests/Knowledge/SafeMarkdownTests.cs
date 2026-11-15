using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Tests;

public class SafeMarkdownTests
{
    /// <summary>D13: links point at the story's Knowledgebase tab instead of KGB's own route.</summary>
    static readonly KbLinkBuilder L = new("My Story");
    const string Kb = "story/My%20Story?tab=kb&amp;p=";

    static string? Resolve(string target) => target switch
    {
        "Welcome" => "Welcome.md",
        "Getting Started/Install" => "Getting Started/Install.md",
        _ => null,
    };

    [Fact]
    public void ForChat_RemovesImages()
    {
        var html = SafeMarkdown.ForChat("Look: ![a cat](https://evil.example/cat.png) and ![](local.png)", L, Resolve);

        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("evil.example", html);
        Assert.Contains("img-placeholder", html);
        Assert.Contains("a cat", html);
    }

    [Theory]
    [InlineData("[click me](https://evil.example/phish)", "https://evil.example/phish")]
    [InlineData("<https://evil.example/auto>", "https://evil.example/auto")]
    [InlineData("Visit https://evil.example/bare today", "https://evil.example/bare")]
    [InlineData("[mail](mailto:someone@evil.example)", "mailto:someone@evil.example")]
    [InlineData("[js](javascript://alert(1))", "javascript://alert(1)")]
    public void ForChat_MakesExternalLinksInert(string markdown, string url)
    {
        var html = SafeMarkdown.ForChat(markdown, L, Resolve);

        Assert.DoesNotContain("href=\"" + url, html);
        Assert.DoesNotContain("<a ", html);
        Assert.Contains("class=\"ext\"", html);
    }

    [Fact]
    public void ForChat_KeepsPageLinks()
    {
        var html = SafeMarkdown.ForChat("See [[Welcome]], [[Getting Started/Install|install]] and [this](./?p=Welcome.md) or [that](?p=Other.md).", L, Resolve);

        Assert.Contains("href=\"" + Kb + "Welcome.md\"", html);
        Assert.Contains("href=\"" + Kb + "Getting%20Started%2FInstall.md\"", html);
        Assert.Contains(">install</a>", html);
        Assert.Contains("href=\"" + Kb + "Other.md\"", html);
        Assert.Contains("class=\"cite\"", html);
    }

    [Fact]
    public void UnresolvedWikiLink_LinksToCreate()
    {
        var html = SafeMarkdown.ForChat("Missing [[Roadmap]].", L, Resolve);

        Assert.Contains("class=\"wu\"", html);
        Assert.Contains(Kb + "Roadmap&amp;create=1", html);
        Assert.Contains("(unresolved)", html);
    }

    [Fact]
    public void RawHtml_IsNeverRendered()
    {
        var html = SafeMarkdown.ForChat("<script>alert(1)</script>\n\nText <b onmouseover=\"x()\">bold</b> <img src=x onerror=alert(1)>", L, Resolve);

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<b ", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void LinkText_AndUrls_AreEncoded()
    {
        var html = SafeMarkdown.ForChat("[\"><script>x</script>](https://a.example/\"onmouseover=\"x)", L, Resolve);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("\"onmouseover", html);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JavaScript:alert(1))")]
    [InlineData("[x](data:text/html,<script>alert(1)</script>)")]
    [InlineData("[x](vbscript:msgbox)")]
    public void ScriptUrls_NeverBecomeLinks(string markdown)
    {
        foreach (var html in new[]
                 {
                     SafeMarkdown.ForChat(markdown, L, Resolve),
                     SafeMarkdown.ForPreview(markdown, L, Resolve, url => null, "Page.md"),
                 })
        {
            Assert.DoesNotContain("href=\"javascript", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("href=\"data", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("href=\"vbscript", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script", html);
        }
    }

    [Fact]
    public void ForPreview_OpensExternalLinksSafely()
    {
        var html = SafeMarkdown.ForPreview("[site](https://example.com)", L, Resolve);

        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html);
    }

    [Fact]
    public void ForPreview_ResolvesRelativeLinks_AndDropsImages()
    {
        var html = SafeMarkdown.ForPreview("[rel](Other.md) [bad](Nope.md) ![pic](https://x.example/p.png)", L, Resolve,
            url => url == "Other.md" ? "Folder/Other.md" : null, "Folder/Page.md");

        Assert.Contains("href=\"" + Kb + "Folder%2FOther.md\"", html);
        Assert.Contains("wu-text", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void FrontMatter_IsNotRendered()
    {
        var html = SafeMarkdown.ForPreview("---\ntitle: Hidden\nsecret: value\n---\n# Visible", L, Resolve);
        Assert.DoesNotContain("secret", html);
        Assert.Contains("Visible", html);
    }

    [Fact]
    public void WikiLinks_InCode_AreNotLinks()
    {
        var html = SafeMarkdown.ForChat("`[[Welcome]]`", L, Resolve);
        Assert.DoesNotContain("<a ", html);
        Assert.Contains("[[Welcome]]", html);
    }

    /// <summary>SEC-002: Preview makes only http, https and mailto clickable; script and data URLs render inert.</summary>
    [Theory]
    [InlineData("[x](javascript://%0aalert(1))")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<vbscript:msgbox(1)>")]
    [InlineData("<data:text/html,hi>")]
    [InlineData("[x]( JAVASCRIPT://%0aalert(1))")]
    [InlineData("[x](file:///C:/Windows/win.ini)")]
    public void ForPreview_NeverEmitsScriptUrls(string markdown)
    {
        var html = SafeMarkdown.ForPreview(markdown, L, Resolve);

        Assert.DoesNotContain("href=\"javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"vbscript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"file", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ext-link", html);
    }

    [Theory]
    [InlineData("<https://example.com>", "https://example.com")]
    [InlineData("[mail](mailto:someone@example.com)", "mailto:someone@example.com")]
    [InlineData("<someone@example.com>", "mailto:someone@example.com")]
    [InlineData("[site](http://example.com/a?b=c)", "http://example.com/a?b=c")]
    public void ForPreview_KeepsSafeLinksClickable(string markdown, string href) =>
        Assert.Contains("href=\"" + href + "\"", SafeMarkdown.ForPreview(markdown, L, Resolve));
}
