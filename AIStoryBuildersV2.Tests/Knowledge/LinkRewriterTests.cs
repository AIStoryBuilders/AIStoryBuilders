using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Markdown;

namespace AIStoryBuildersV2.Tests;

/// <summary>The rewrite cases of §5.5, end to end through the workspace.</summary>
public sealed class LinkRewriterTests : IDisposable
{
    readonly WorkspaceFixture _fx = new(seed: false);

    public void Dispose() => _fx.Dispose();

    async Task PagesAsync(params (string Path, string Markdown)[] pages)
    {
        foreach (var (path, markdown) in pages) await _fx.Workspace.SavePageAsync(path, markdown, 0);
    }

    Task<string> TextAsync(string path) => _fx.Workspace.ReadPageAsync(path).ContinueWith(t => t.Result.Markdown);

    [Fact]
    public async Task BareWikiName_IsReplacedWithTheNewBareName()
    {
        await PagesAsync(("A/Note.md", "# Note"), ("B/Other.md", "See [[Note]] and [[note#Part|the part]]."));

        var result = await _fx.Workspace.RenameAsync("A/Note.md", "Memo");

        Assert.Equal(1, result.PagesChanged);
        Assert.Equal("See [[Memo]] and [[Memo#Part|the part]].", await TextAsync("B/Other.md"));
    }

    [Fact]
    public async Task PathWikiLink_KeepsThePathForm()
    {
        await PagesAsync(("A/Note.md", "# Note"), ("Index.md", "[[A/Note]] [[A/Note.md|alias]]"));

        await _fx.Workspace.MoveAsync("A/Note.md", "");
        Assert.Equal("[[Note]] [[Note|alias]]", await TextAsync("Index.md"));

        // Bare names that still resolve to the moved page by file name stay as they are.
        await _fx.Workspace.CreateFolderAsync("C");
        await _fx.Workspace.MoveAsync("Note.md", "C");
        Assert.Equal("[[Note]] [[Note|alias]]", await TextAsync("Index.md"));
        Assert.Equal("C/Note.md", await _fx.Workspace.Graph.ResolveAsync("Index.md", "Note"));

        // A path link that no longer resolves keeps its path form.
        await _fx.Workspace.SavePageAsync("Paths.md", "[[C/Note]]", 0);
        await _fx.Workspace.CreateFolderAsync("D");
        await _fx.Workspace.MoveAsync("C/Note.md", "D");
        Assert.Equal("[[D/Note]]", await TextAsync("Paths.md"));
    }

    [Fact]
    public async Task AmbiguousNewName_WritesTheFullPath()
    {
        await PagesAsync(("A/Note.md", "# Note"), ("C/Target.md", "# Other target"), ("B/Other.md", "See [[Note]]."));

        await _fx.Workspace.RenameAsync("A/Note.md", "Target");

        Assert.Equal("See [[A/Target]].", await TextAsync("B/Other.md"));
        Assert.Equal("A/Target.md", await _fx.Workspace.Graph.ResolveAsync("B/Other.md", "A/Target"));
    }

    [Fact]
    public async Task SameFolderName_StaysBare_EvenWhenAmbiguousElsewhere()
    {
        await PagesAsync(("A/Note.md", "# Note"), ("A/Other.md", "See [[Note]]."), ("C/Target.md", "# Other target"));

        await _fx.Workspace.RenameAsync("A/Note.md", "Target");

        Assert.Equal("See [[Target]].", await TextAsync("A/Other.md"));
    }

    [Fact]
    public async Task RelativeMarkdownLink_InAnotherPage_IsRecomputed()
    {
        await PagesAsync(("A/My Note.md", "# Note"), ("B/Other.md", "See [the note](../A/My%20Note.md#intro) and [root](../Root.md)."), ("Root.md", "# Root"));
        await _fx.Workspace.CreateFolderAsync("C/D");

        await _fx.Workspace.MoveAsync("A/My Note.md", "C/D");

        Assert.Equal("See [the note](../C/D/My%20Note.md#intro) and [root](../Root.md).", await TextAsync("B/Other.md"));
        Assert.Equal("C/D/My Note.md", Assert.Single((await _fx.Workspace.Graph.GetLinksAsync("B/Other.md")).Outgoing,
            l => l.RawTarget.Contains("Note")).ResolvedPath);
    }

    [Fact]
    public async Task RelativeLinks_InsideTheMovedPage_ThatPointOutside_AreRecomputed()
    {
        await PagesAsync(("A/Note.md", "Up [o](../B/Other.md), sibling [s](Sibling.md), wiki [[Other]]."), ("A/Sibling.md", "# S"), ("B/Other.md", "# O"));

        await _fx.Workspace.MoveAsync("A/Note.md", "");

        // The sibling stays behind, so its link is recomputed too; the wiki link still resolves by name.
        Assert.Equal("Up [o](B/Other.md), sibling [s](A/Sibling.md), wiki [[Other]].", await TextAsync("Note.md"));
    }

    [Fact]
    public async Task MovingAFolder_KeepsLinksBetweenItsOwnPages()
    {
        await PagesAsync(
            ("P/One.md", "To [two](Two.md) and [[P/Two]] and [out](../Out.md)."),
            ("P/Two.md", "Back to [[One]]."),
            ("Out.md", "In: [one](P/One.md)."));
        await _fx.Workspace.CreateFolderAsync("Archive");

        await _fx.Workspace.MoveAsync("P", "Archive");

        Assert.Equal("To [two](Two.md) and [[Archive/P/Two]] and [out](../../Out.md).", await TextAsync("Archive/P/One.md"));
        Assert.Equal("Back to [[One]].", await TextAsync("Archive/P/Two.md"));
        Assert.Equal("In: [one](Archive/P/One.md).", await TextAsync("Out.md"));
        Assert.Empty(await _fx.Workspace.Graph.GetUnresolvedLinksAsync());
    }

    [Fact]
    public async Task TitleAndAliasLinks_AreUnchanged()
    {
        await PagesAsync(("T.md", "---\ntitle: Fancy Title\naliases: [Nick]\n---\n# T"), ("Ref.md", "[[Fancy Title]] and [[Nick]] and [[T]]"));

        await _fx.Workspace.RenameAsync("T.md", "U");

        Assert.Equal("[[Fancy Title]] and [[Nick]] and [[U]]", await TextAsync("Ref.md"));
        Assert.Empty(await _fx.Workspace.Graph.GetUnresolvedLinksAsync());
    }

    [Fact]
    public async Task UnrelatedLinksAndCode_AreUntouched()
    {
        const string text = "[[Note]] `[[Note]]` [[Missing]] [x](https://example.com/Note.md)\n\n```\n[[Note]]\n```\n";
        await PagesAsync(("Note.md", "# Note"), ("Ref.md", text));

        await _fx.Workspace.RenameAsync("Note.md", "Memo");

        Assert.Equal(text.Replace("[[Note]] `", "[[Memo]] `"), await TextAsync("Ref.md"));
    }

    [Fact]
    public async Task SeveralLinksOnOneLine_AreAllRewritten_BackToFront()
    {
        await PagesAsync(("Long Name.md", "# L"), ("Ref.md", "[[Long Name]][[Long Name|a]] [l](Long%20Name.md) [[Long Name#h]]"));

        await _fx.Workspace.RenameAsync("Long Name.md", "X");

        Assert.Equal("[[X]][[X|a]] [l](X.md) [[X#h]]", await TextAsync("Ref.md"));
    }

    [Fact]
    public async Task Rewrite_ReturnsTheSameText_WhenNothingMoved()
    {
        await PagesAsync(("A.md", "[[B]]"), ("B.md", "# B"));
        await _fx.Workspace.Graph.RebuildAsync();
        var snapshot = _fx.Graph.Snapshot;
        var moved = new Dictionary<string, string>();

        var text = LinkRewriter.Rewrite("[[B]]", "A.md", snapshot, moved, snapshot.Directory);

        Assert.Equal("[[B]]", text);
    }

    [Fact]
    public void MapMovedPages_UsesThePrefixWithASlash()
    {
        var map = LinkRewriter.MapMovedPages(["P/a.md", "P/Sub/b.md", "Px/c.md", "P.md"], "P", "Q");
        Assert.Equal(new Dictionary<string, string> { ["P/a.md"] = "Q/a.md", ["P/Sub/b.md"] = "Q/Sub/b.md" }, map);
    }
}
