using AIStoryBuildersV2.Shared.Knowledge;

namespace AIStoryBuildersV2.Tests;

/// <summary>Plan §11: per-story KB provider (D12, D13), WASM tick tracking, and KB pages through .stybld.</summary>
public class StoryKnowledgeBaseTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly RecordingPersistence _persistence = new();

    public void Dispose() => _dir.Dispose();

    StoryKnowledgeBaseProvider Provider(bool browser = false) =>
        new(new TestHost(_dir.Path) { IsBrowser = browser }, new PermanentFileDeleter(), _persistence);

    [Fact]
    public async Task Two_stories_have_isolated_roots_and_state()
    {
        var provider = Provider();
        var a = await provider.OpenAsync("Story A");
        var b = await provider.OpenAsync("Story B");

        await a.Workspace.SavePageAsync("Lore/Necklace.md", "# Necklace\n", 0);
        a.State.ExpandedFolders.Add("Lore");

        Assert.Equal(Path.Combine(_dir.Path, "Story A", "KnowledgeBase"), a.RootPath);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "Story A", "KnowledgeBase", "Lore", "Necklace.md")));
        Assert.Empty(await b.Workspace.GetAllPagePathsAsync());
        Assert.Empty(b.State.ExpandedFolders);
        Assert.False(await a.IsEmptyAsync());
        Assert.True(await b.IsEmptyAsync());
        Assert.Equal("story/Story%20A?tab=kb&p=Lore%2FNecklace.md", a.Links.PageHref("Lore/Necklace.md"));
    }

    [Fact]
    public async Task The_folder_is_not_created_until_the_first_write()
    {
        var kb = await Provider().OpenAsync("Story A");

        Assert.Empty(await kb.Workspace.GetChildrenAsync(""));
        Assert.Empty(await kb.Workspace.GetAllPagePathsAsync());
        Assert.Equal(0, (await kb.Graph.GetStatsAsync()).Pages);
        Assert.False(Directory.Exists(kb.RootPath));

        await kb.Workspace.CreateFolderAsync("Lore");
        Assert.True(Directory.Exists(Path.Combine(kb.RootPath, "Lore")));
    }

    [Fact]
    public async Task Writes_mark_the_story_dirty()
    {
        var kb = await Provider().OpenAsync("Story A");

        await kb.Workspace.SavePageAsync("Necklace.md", "# Necklace\n", 0);

        Assert.Contains("Story A", _persistence.Dirty);
    }

    [Fact]
    public async Task Open_is_cached_and_the_last_close_disposes()
    {
        var provider = Provider();
        var first = await provider.OpenAsync("Story A");
        var second = await provider.OpenAsync("story a");
        Assert.Same(first, second);

        await provider.CloseAsync("Story A");
        Assert.Same(first, await provider.OpenAsync("Story A"));   // still one user left, then this one
        await provider.CloseAsync("Story A");
        await provider.CloseAsync("Story A");

        _persistence.Dirty.Clear();
        await first.Workspace.SavePageAsync("After.md", "x", null);
        Assert.Empty(_persistence.Dirty);                           // disposed: no longer wired to persistence
        Assert.NotSame(first, await provider.OpenAsync("Story A"));
    }

    [Fact]
    public async Task Browser_stores_track_ticks_in_memory()
    {
        var kb = await Provider(browser: true).OpenAsync("Story A");

        var created = await kb.Workspace.SavePageAsync("Necklace.md", "one", 0);
        var page = await kb.Workspace.ReadPageAsync("Necklace.md");
        Assert.Equal(created.ModifiedTicks, page.ModifiedTicks);

        var saved = await kb.Workspace.SavePageAsync("Necklace.md", "two", page.ModifiedTicks);
        Assert.Equal(SaveStatus.Saved, saved.Status);
        Assert.True(saved.ModifiedTicks > created.ModifiedTicks);

        var stale = await kb.Workspace.SavePageAsync("Necklace.md", "three", page.ModifiedTicks);
        Assert.Equal(SaveStatus.Conflict, stale.Status);
        Assert.Equal("two", (await kb.Workspace.ReadPageAsync("Necklace.md")).Markdown);
    }

    [Fact]
    public async Task Tick_tracking_seeds_existing_pages_and_follows_renames()
    {
        var root = Path.Combine(_dir.Path, "Story A", "KnowledgeBase");
        Directory.CreateDirectory(Path.Combine(root, "Lore"));
        File.WriteAllText(Path.Combine(root, "Lore", "Necklace.md"), "# Necklace\n");
        var store = await TickTrackingKnowledgeStore.CreateAsync(new FileSystemKnowledgeStore(new KnowledgeStoreOptions(root), new PermanentFileDeleter()));

        var before = await store.ReadPageAsync("Lore/Necklace.md");
        Assert.True(before.ModifiedTicks > 0);
        Assert.Equal(SaveStatus.AlreadyExists, (await store.SavePageAsync("Lore/Necklace.md", "x", 0)).Status);

        var renamed = await store.RenameAsync("Lore", "Items");
        var after = await store.ReadPageAsync("Items/Necklace.md");
        Assert.Equal("Items", renamed);
        Assert.Equal(SaveStatus.Saved, (await store.SavePageAsync("Items/Necklace.md", "y", after.ModifiedTicks)).Status);

        await store.DeleteAsync("Items");
        Assert.Equal(SaveStatus.Created, (await store.SavePageAsync("Items/Necklace.md", "z", 0)).Status);
    }

    [Fact]
    public async Task Knowledge_base_pages_round_trip_through_stybld()
    {
        using var source = new ServiceFixture();
        using var target = new ServiceFixture(writeBefore: false);
        var sourceKb = await new StoryKnowledgeBaseProvider(new TestHost(source.Dir.Path), new PermanentFileDeleter(), source.Persistence)
            .OpenAsync(FixtureFactory.Title);
        await sourceKb.Workspace.SavePageAsync("Lore/Necklace.md", "---\ntitle: The Necklace\n---\nSee [[Hrafnkel]].\n", 0);
        await sourceKb.Workspace.SavePageAsync("Hrafnkel.md", "# Hrafnkel\nA smith.\n", 0);

        var story = (await source.Service.GetStorysAsync()).Single();
        await target.Service.ImportProjectAsync(source.Service.ExportProject(story));

        var targetKb = await new StoryKnowledgeBaseProvider(new TestHost(target.Dir.Path), new PermanentFileDeleter(), target.Persistence)
            .OpenAsync(FixtureFactory.Title);
        Assert.Equal(["Hrafnkel.md", "Lore/Necklace.md"], (await targetKb.Workspace.GetAllPagePathsAsync()).Order(StringComparer.Ordinal));
        Assert.Equal("# Hrafnkel\nA smith.\n", (await targetKb.Workspace.ReadPageAsync("Hrafnkel.md")).Markdown);
        var links = await targetKb.Graph.GetLinksAsync("Lore/Necklace.md");
        Assert.Contains(links.Outgoing, l => l.ResolvedPath == "Hrafnkel.md");
    }
}
