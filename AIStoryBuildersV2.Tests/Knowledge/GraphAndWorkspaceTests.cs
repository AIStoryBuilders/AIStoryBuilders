using System.Text.RegularExpressions;
using AIStoryBuildersV2.Shared.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStoryBuildersV2.Tests;

public sealed class GraphAndWorkspaceTests : IDisposable
{
    readonly WorkspaceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    IKnowledgeGraphQuery Graph => _fx.Workspace.Graph;

    /// <summary>The text outside `code spans`, where links are examples and are never rewritten.</summary>
    static string WithoutCode(string markdown) => Regex.Replace(markdown, "`[^`\n]*`", "");

    [Fact]
    public async Task Seed_CreatesTheExpectedPages_OnlyWhenEmpty()
    {
        Assert.Equal(
            ["Getting Started/Install.md", "Getting Started/Linking Pages.md", "Getting Started/Using Chat.md", "Welcome.md"],
            (await _fx.Workspace.GetAllPagePathsAsync()).Order(StringComparer.Ordinal));
        Assert.False(await KnowledgeSeeder.SeedIfEmptyAsync(_fx.Store));
    }

    [Fact]
    public async Task Rebuild_CountsPagesLinksAndUnresolved()
    {
        var stats = await Graph.RebuildAsync();
        Assert.Equal(4, stats.Pages);
        Assert.True(stats.Links > 5);
        Assert.Equal(stats, await Graph.GetStatsAsync());
        var unresolved = await Graph.GetUnresolvedLinksAsync();
        Assert.Equal(stats.Unresolved, unresolved.Count);
        Assert.Contains(unresolved, l => l.RawTarget == "Roadmap" && l.SourcePath == "Getting Started/Linking Pages.md");
    }

    [Fact]
    public async Task Backlinks_And_Outgoing()
    {
        var links = await Graph.GetLinksAsync("Welcome.md");

        Assert.Contains(links.Outgoing, l => l.ResolvedPath == "Getting Started/Install.md" && l.Kind == LinkKind.Wiki);
        Assert.Contains(links.Outgoing, l => l.RawTarget == "Chat" && l.ResolvedPath == "Getting Started/Using Chat.md");
        Assert.Equal(["Getting Started/Install.md", "Getting Started/Linking Pages.md"],
            links.Backlinks.Select(l => l.SourcePath).Distinct().Order(StringComparer.Ordinal));
        Assert.All(links.Backlinks, l => Assert.True(l.Line > 0));
    }

    [Fact]
    public async Task UnresolvedLink_BecomesResolved_WhenThePageIsCreated()
    {
        Assert.Contains(await Graph.GetUnresolvedLinksAsync(), l => l.RawTarget == "Roadmap");

        await _fx.Workspace.SavePageAsync("Roadmap.md", "# Roadmap\n", 0);

        Assert.DoesNotContain(await Graph.GetUnresolvedLinksAsync(), l => l.RawTarget == "Roadmap");
        var back = (await Graph.GetLinksAsync("Roadmap.md")).Backlinks;
        Assert.Equal("Getting Started/Linking Pages.md", Assert.Single(back).SourcePath);

        await _fx.Workspace.DeleteAsync("Roadmap.md");
        Assert.Contains(await Graph.GetUnresolvedLinksAsync(), l => l.RawTarget == "Roadmap");
        Assert.Null(await Graph.GetPageInfoAsync("Roadmap.md"));
    }

    [Fact]
    public async Task Search_RanksTitleAboveBody_AndBreaksTiesByNewest()
    {
        var hits = await Graph.SearchAsync("install");
        Assert.Equal("Getting Started/Install.md", hits[0].Path);
        Assert.Equal("Install", hits[0].Title);
        Assert.True(hits[0].Score > hits[1].Score);

        await _fx.Workspace.SavePageAsync("Old.md", "zebra", 0);
        await Task.Delay(5);
        await _fx.Workspace.SavePageAsync("New.md", "zebra", 0);
        var tie = await Graph.SearchAsync("zebra");
        Assert.Equal(["New.md", "Old.md"], tie.Select(h => h.Path));
        Assert.Equal(tie[0].Score, tie[1].Score);
    }

    [Fact]
    public async Task Search_ScoresEachSignal()
    {
        await _fx.Workspace.SavePageAsync("P/Plain.md", "---\ntitle: Kumquat\naliases: [kumquat alias]\ntags: [kumquat]\n---\nkumquat kumquat kumquat kumquat kumquat kumquat kumquat", 0);
        await _fx.Workspace.SavePageAsync("Kumquat Folder/x.md", "nothing", 0);

        var hits = await Graph.SearchAsync("kumquat");

        var top = hits.Single(h => h.Path == "P/Plain.md");
        Assert.Equal(10 + 6 + 4 + 5, top.Score);              // title, alias, tag, body capped at 5
        Assert.Equal(3, hits.Single(h => h.Path == "Kumquat Folder/x.md").Score);
        Assert.Empty(await Graph.SearchAsync("   "));
        Assert.Single(await Graph.SearchAsync("kumquat", top: 1));
    }

    [Fact]
    public async Task Search_SnippetIsAbout160Characters()
    {
        var body = string.Join(' ', Enumerable.Repeat("filler", 80)) + " needle " + string.Join(' ', Enumerable.Repeat("filler", 80));
        await _fx.Workspace.SavePageAsync("Long.md", body, 0);

        var hit = (await Graph.SearchAsync("needle")).Single();

        Assert.Contains("needle", hit.Snippet);
        Assert.InRange(hit.Snippet.Length, 150, 165);
        Assert.StartsWith("…", hit.Snippet);
        Assert.EndsWith("…", hit.Snippet);
    }

    [Fact]
    public async Task Neighborhood_FollowsLinksBothWays()
    {
        var one = await Graph.GetNeighborhoodAsync("Getting Started/Using Chat.md", 1);
        var paths = one.Pages.Select(p => p.Path).Order(StringComparer.Ordinal).ToList();
        Assert.Contains("Getting Started/Using Chat.md", paths);
        Assert.Contains("Welcome.md", paths);                       // backlink via the "Chat" alias
        Assert.Contains("Getting Started/Linking Pages.md", paths); // outgoing
        Assert.DoesNotContain("Getting Started/Install.md", paths);
        Assert.All(one.Links, l => Assert.NotNull(l.ResolvedPath));

        var two = await Graph.GetNeighborhoodAsync("Getting Started/Using Chat.md", 2);
        Assert.Contains(two.Pages, p => p.Path == "Getting Started/Install.md");
    }

    [Fact]
    public async Task PageInfo_And_Resolve()
    {
        var info = await Graph.GetPageInfoAsync("Getting Started/Using Chat.md");
        Assert.NotNull(info);
        Assert.Equal("Using Chat", info.Title);
        Assert.Equal(["Chat"], info.Aliases);
        Assert.Contains("chat", info.Tags);

        Assert.Equal("Getting Started/Using Chat.md", await Graph.ResolveAsync("Welcome.md", "Chat"));
        Assert.Null(await Graph.ResolveAsync("Welcome.md", "Roadmap"));
    }

    [Fact]
    public async Task Save_RaisesChanged_AndUpdatesTheGraph()
    {
        var changes = new List<KnowledgeChange>();
        _fx.Workspace.Changed += changes.Add;
        await Graph.RebuildAsync();

        var page = await _fx.Workspace.ReadPageAsync("Welcome.md");
        await _fx.Workspace.SavePageAsync("Welcome.md", page.Markdown + "\nSee [[Brand New]].\n", page.ModifiedTicks);
        var conflict = await _fx.Workspace.SavePageAsync("Welcome.md", "stale", page.ModifiedTicks);
        await _fx.Workspace.CreateFolderAsync("Empty");

        Assert.Equal(SaveStatus.Conflict, conflict.Status);
        Assert.Equal([new KnowledgeChange(ChangeKind.Updated, "Welcome.md"), new KnowledgeChange(ChangeKind.FolderCreated, "Empty")], changes);
        Assert.Contains(await Graph.GetUnresolvedLinksAsync(), l => l.RawTarget == "Brand New");
    }

    [Fact]
    public async Task ConcurrentWrites_AreSerialized()
    {
        await Graph.RebuildAsync();
        var tasks = Enumerable.Range(0, 40).Select(i => _fx.Workspace.SavePageAsync($"Bulk/P{i}.md", $"# P{i}\n[[Welcome]]", 0)).ToList();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(SaveStatus.Created, r.Status));
        Assert.Equal(44, (await Graph.GetStatsAsync()).Pages);
        Assert.Equal(40, (await Graph.GetLinksAsync("Welcome.md")).Backlinks.Count(b => b.SourcePath.StartsWith("Bulk/")));
    }

    [Fact]
    public async Task ConcurrentSavesOfOnePage_OnlyOneWinsWithTheSameTicks()
    {
        var page = await _fx.Workspace.ReadPageAsync("Welcome.md");
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => _fx.Workspace.SavePageAsync("Welcome.md", $"version {i}", page.ModifiedTicks)));

        Assert.Single(results, r => r.Status == SaveStatus.Saved);
        Assert.Equal(9, results.Count(r => r.Status == SaveStatus.Conflict));
    }

    /// <summary>Only what the graph needs; pages live in memory like the WASM file system.</summary>
    internal sealed class MemoryStore(Dictionary<string, string> pages) : IKnowledgeStore
    {
        public event Action<KnowledgeChange>? ExternalChange { add { } remove { } }
        public Task<IReadOnlyList<string>> GetAllPagePathsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(pages.Keys.ToList());
        public Task<PageContent> ReadPageAsync(string pagePath, CancellationToken ct = default) => Task.FromResult(new PageContent(pagePath, pages[pagePath], 1));
        public Task<IReadOnlyList<KnowledgeNode>> GetChildrenAsync(string folderPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SaveResult> SavePageAsync(string pagePath, string markdown, long? expectedModifiedTicks, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateFolderAsync(string folderPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> RenameAsync(string path, string newName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> MoveAsync(string path, string newParentFolder, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FolderStats> GetFolderStatsAsync(string folderPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(pages.ContainsKey(path));
    }

    // The 5,000-page rebuild timing test is in KnowledgePerformanceTests, which runs without parallel tests.

    // ------------------------------------------------------------ rename and move

    [Fact]
    public async Task RenameInstall_RewritesLinksInOtherPages()
    {
        var changes = new List<KnowledgeChange>();
        _fx.Workspace.Changed += changes.Add;
        var installBefore = (await _fx.Workspace.ReadPageAsync("Getting Started/Install.md")).Markdown;

        var result = await _fx.Workspace.RenameAsync("Getting Started/Install.md", "Setup");

        Assert.Equal("Getting Started/Setup.md", result.NewPath);
        Assert.Equal(2, result.PagesChanged);
        Assert.Empty(result.FailedPages);
        Assert.Contains(new KnowledgeChange(ChangeKind.Renamed, "Getting Started/Setup.md", "Getting Started/Install.md"), changes);

        var welcome = (await _fx.Workspace.ReadPageAsync("Welcome.md")).Markdown;
        Assert.Contains("[[Getting Started/Setup]]", welcome);
        Assert.DoesNotContain("Install]]", welcome);
        Assert.Contains("[[Getting Started/Linking Pages]]", welcome);
        Assert.Contains("[[Chat]]", welcome);

        var linking = (await _fx.Workspace.ReadPageAsync("Getting Started/Linking Pages.md")).Markdown;
        Assert.Contains("[[Getting Started/Setup]]", linking);
        Assert.Contains("[[Setup#API key]]", linking);
        Assert.Contains("[[Setup|setup guide]]", linking);
        Assert.DoesNotContain("[[Install", WithoutCode(linking));
        Assert.DoesNotContain("[[Getting Started/Install]]", WithoutCode(linking));
        // Links inside code spans are examples and stay as they are.
        Assert.Contains("`[[Install#API key]]`", linking);
        Assert.Contains("[[Roadmap]]", linking);

        // The renamed page itself didn't need changes.
        Assert.Equal(installBefore, (await _fx.Workspace.ReadPageAsync("Getting Started/Setup.md")).Markdown);

        Assert.Empty((await Graph.GetLinksAsync("Getting Started/Install.md")).Backlinks);
        Assert.Equal(["Getting Started/Linking Pages.md", "Welcome.md"],
            (await Graph.GetLinksAsync("Getting Started/Setup.md")).Backlinks.Select(b => b.SourcePath).Distinct().Order(StringComparer.Ordinal));
        Assert.DoesNotContain(await Graph.GetUnresolvedLinksAsync(), l => l.RawTarget.Contains("Install"));
    }

    [Fact]
    public async Task RenameInstall_IsIdempotent_AndReversible()
    {
        var before = new Dictionary<string, string>();
        foreach (var p in await _fx.Workspace.GetAllPagePathsAsync()) before[p] = (await _fx.Workspace.ReadPageAsync(p)).Markdown;

        await _fx.Workspace.RenameAsync("Getting Started/Install.md", "Setup");
        var afterFirst = new Dictionary<string, string>();
        foreach (var p in await _fx.Workspace.GetAllPagePathsAsync()) afterFirst[p] = (await _fx.Workspace.ReadPageAsync(p)).Markdown;

        var again = await _fx.Workspace.RenameAsync("Getting Started/Setup.md", "Setup");
        Assert.Equal("Getting Started/Setup.md", again.NewPath);
        Assert.Equal(0, again.PagesChanged);
        foreach (var (p, text) in afterFirst) Assert.Equal(text, (await _fx.Workspace.ReadPageAsync(p)).Markdown);

        var back = await _fx.Workspace.RenameAsync("Getting Started/Setup.md", "Install");
        Assert.Equal(2, back.PagesChanged);
        foreach (var (p, text) in before) Assert.Equal(text, (await _fx.Workspace.ReadPageAsync(p)).Markdown);
    }

    [Fact]
    public async Task RenameFolder_RewritesPathLinks_AndKeepsBareNames()
    {
        var result = await _fx.Workspace.RenameAsync("Getting Started", "Guide");

        Assert.Equal("Guide", result.NewPath);
        Assert.Empty(result.FailedPages);
        var welcome = (await _fx.Workspace.ReadPageAsync("Welcome.md")).Markdown;
        Assert.Contains("[[Guide/Install]]", welcome);
        Assert.Contains("[[Guide/Linking Pages]]", welcome);

        var linking = (await _fx.Workspace.ReadPageAsync("Guide/Linking Pages.md")).Markdown;
        Assert.Contains("[[Guide/Install]]", linking);
        Assert.Contains("[[Install#API key]]", linking);
        Assert.Contains("[[Welcome]]", linking);
        Assert.DoesNotContain("Getting Started/", WithoutCode(welcome + linking));
        Assert.Equal(1, (await Graph.GetUnresolvedLinksAsync()).Count(l => l.RawTarget == "Roadmap"));
        Assert.Single(await Graph.GetUnresolvedLinksAsync());
    }

    [Fact]
    public async Task MovePage_ToTheRoot_RewritesLinks()
    {
        var result = await _fx.Workspace.MoveAsync("Getting Started/Install.md", "");

        Assert.Equal("Install.md", result.NewPath);
        Assert.Contains("[[Install]]", (await _fx.Workspace.ReadPageAsync("Welcome.md")).Markdown);
        var linking = (await _fx.Workspace.ReadPageAsync("Getting Started/Linking Pages.md")).Markdown;
        Assert.Contains("[[Install#API key]]", linking);  // still unique by name
        Assert.Single(await Graph.GetUnresolvedLinksAsync());
    }

    [Fact]
    public async Task Rename_WithoutTheExtension_FindsThePage()
    {
        var result = await _fx.Workspace.RenameAsync("Getting Started/Install", "Setup");
        Assert.Equal("Getting Started/Setup.md", result.NewPath);
        Assert.Equal(2, result.PagesChanged);
    }

    [Fact]
    public async Task Rename_IntoAnExistingName_Throws_AndChangesNothing()
    {
        await Assert.ThrowsAsync<KnowledgeAlreadyExistsException>(() => _fx.Workspace.RenameAsync("Getting Started/Install.md", "Linking Pages"));
        Assert.True(await _fx.Workspace.ExistsAsync("Getting Started/Install.md"));
        Assert.Contains("[[Getting Started/Install]]", (await _fx.Workspace.ReadPageAsync("Welcome.md")).Markdown);
    }

    [Fact]
    public async Task DeleteFolder_RemovesItsPagesFromTheGraph()
    {
        await Graph.RebuildAsync();
        await _fx.Workspace.DeleteAsync("Getting Started");

        Assert.Equal(1, (await Graph.GetStatsAsync()).Pages);
        Assert.All(await Graph.GetUnresolvedLinksAsync(), l => Assert.Equal("Welcome.md", l.SourcePath));
    }

    // KGB's ExternalChange_UpdatesTheGraph is not ported: it tests PersistingKnowledgeStore, which D12 leaves out.
}
