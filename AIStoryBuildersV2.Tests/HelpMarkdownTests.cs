using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Tests;

public class HelpMarkdownTests
{
    [Fact]
    public void LoadGuide_ListsTheGuidesSectionsWithGitHubAnchors()
    {
        var guide = HelpMarkdown.LoadGuide();

        Assert.Contains(guide.Sections, s => s.Id == "before-you-start" && s.Title == "Before you start");
        Assert.Contains(guide.Sections, s => s.Id == "1-connect-an-ai-provider" && s.Title == "1. Connect an AI provider");
        Assert.Contains(guide.Sections, s => s.Id == "12-export-back-up-and-maintain-a-story");
        Assert.Contains(guide.Sections, s => s.Id == "troubleshooting");
        Assert.Contains("id=\"1-connect-an-ai-provider\"", guide.Html);
    }

    [Fact]
    public void LoadGuide_EveryInPageLinkHasATarget()
    {
        var guide = HelpMarkdown.LoadGuide();

        var targets = System.Text.RegularExpressions.Regex.Matches(guide.Html, "href=\"help#([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(targets);
        Assert.All(targets, id => Assert.Contains($"id=\"{id}\"", guide.Html));
    }

    [Fact]
    public void LoadGuide_EveryScreenshotIsShipped()
    {
        var guide = HelpMarkdown.LoadGuide();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Docs"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var docs = Path.Combine(dir.FullName, "Docs", "images", "user-guide");

        var files = System.Text.RegularExpressions.Regex.Matches(guide.Html, $"src=\"{HelpMarkdown.ImageBase}([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(docs, f)), $"Missing screenshot {f}"));
    }

    [Fact]
    public void Render_PointsAnchorsAtTheHelpPage()
    {
        var html = HelpMarkdown.Render("See [Settings](#1-connect-an-ai-provider).").Html;

        Assert.Contains("<a href=\"help#1-connect-an-ai-provider\">Settings</a>", html);
    }

    [Fact]
    public void Render_ShowsOnlyTheGuidesOwnImages()
    {
        var html = HelpMarkdown.Render("![Shot](images/user-guide/01.png) ![Away](https://evil.example/x.png) ![Up](images/user-guide/../x.png)").Html;

        Assert.Contains($"<img src=\"{HelpMarkdown.ImageBase}01.png\" alt=\"Shot\"", html);
        Assert.DoesNotContain("evil.example", html);
        Assert.DoesNotContain("../x.png", html);
    }

    [Fact]
    public void Render_DisablesRawHtml()
    {
        var html = HelpMarkdown.Render("<script>alert(1)</script>\n\n[x](javascript:alert(1))").Html;

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void Render_ShowsAMermaidFlowchartAsItsSteps()
    {
        var html = HelpMarkdown.Render("```mermaid\nflowchart LR\n    A[Connect] --> B[Write]\n    B --> A\n```").Html;

        Assert.Contains("asb-help-flow", html);
        Assert.Contains("<span class=\"asb-help-node\">Connect</span>", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<li>").Count);
        Assert.DoesNotContain("flowchart", html);
    }

    [Fact]
    public void Render_KeepsOtherCodeBlocks()
    {
        var html = HelpMarkdown.Render("```bash\ndotnet build\n```").Html;

        Assert.Contains("<pre><code", html);
        Assert.Contains("dotnet build", html);
    }
}
