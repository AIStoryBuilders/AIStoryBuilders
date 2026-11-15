using AIStoryBuildersV2.Shared.Knowledge;

namespace AIStoryBuildersV2.Tests;

public class GraphViewTests
{
    static async Task<GraphSnapshot> SnapshotAsync(Dictionary<string, string> pages)
    {
        var graph = new KnowledgeGraphService(new GraphAndWorkspaceTests.MemoryStore(pages));
        await graph.RebuildAsync();
        return graph.Snapshot;
    }

    static Dictionary<string, string> Sample() => new()
    {
        ["Welcome.md"] = "# Welcome\n\nSee [[Install]] and [[Ideas]].",
        ["Docs/Install.md"] = "# Install\n\nBack to [[Welcome]]. See [[Linking]] and [[Install]].",
        ["Docs/Linking.md"] = "# Linking\n\nSee [[Install#Steps]] and [[Roadmap|the plan]].",
        ["Lonely.md"] = "# Lonely\n\nNo links.",
    };

    [Fact]
    public async Task WholeGraph_HasOneEdgePerConnectedPair_AndMissingTargets()
    {
        var view = GraphViewBuilder.Build(await SnapshotAsync(Sample()), new GraphViewOptions());

        Assert.Equal(["Docs/Install.md", "Docs/Linking.md", "Lonely.md", "Welcome.md"], view.Nodes.Where(n => !n.Missing).Select(n => n.Path));
        Assert.Equal(["Ideas", "Roadmap"], view.Nodes.Where(n => n.Missing).Select(n => n.Label).Order());

        // Welcome<->Install both ways is one edge; Install's link to itself is dropped.
        var pageEdges = view.Edges.Where(e => !e.ToMissing).Select(e => $"{e.From}>{e.To}").ToList();
        Assert.Equal(["Docs/Install.md>Docs/Linking.md", "Docs/Install.md>Welcome.md"], pageEdges);
        Assert.Equal(2, view.Edges.Count(e => e.ToMissing));
        Assert.False(view.Truncated);
        Assert.Equal(4, view.TotalPages);

        var install = view.Nodes.Single(n => n.Path == "Docs/Install.md");
        Assert.Equal(2, install.Degree);
        Assert.Equal("Docs", install.Group);
        Assert.Equal("Install", install.Label);
    }

    [Fact]
    public async Task MissingPages_CanBeHidden()
    {
        var view = GraphViewBuilder.Build(await SnapshotAsync(Sample()), new GraphViewOptions(IncludeMissing: false));
        Assert.DoesNotContain(view.Nodes, n => n.Missing);
        Assert.DoesNotContain(view.Edges, e => e.ToMissing);
    }

    [Fact]
    public async Task AroundPage_FollowsLinksBothWays_UpToTheDepth()
    {
        var snapshot = await SnapshotAsync(Sample());

        var one = GraphViewBuilder.Build(snapshot, new GraphViewOptions("Welcome.md", Depth: 1, IncludeMissing: false));
        Assert.Equal(["Docs/Install.md", "Welcome.md"], one.Nodes.Select(n => n.Path));

        var two = GraphViewBuilder.Build(snapshot, new GraphViewOptions("welcome.md", Depth: 2, IncludeMissing: false));
        Assert.Equal(["Docs/Install.md", "Docs/Linking.md", "Welcome.md"], two.Nodes.Select(n => n.Path));

        // The center page sits in the middle of the picture.
        var center = two.Nodes.Single(n => n.Path == "Welcome.md");
        Assert.Equal((0.0, 0.0), (center.X, center.Y));
    }

    [Fact]
    public async Task AnUnknownCenter_ShowsTheWholeGraph()
    {
        var view = GraphViewBuilder.Build(await SnapshotAsync(Sample()), new GraphViewOptions("Nope.md", IncludeMissing: false));
        Assert.Equal(4, view.Nodes.Count);
    }

    [Fact]
    public async Task BigGraphs_KeepTheBestConnectedPages()
    {
        var pages = new Dictionary<string, string> { ["Hub.md"] = "# Hub\n" + string.Join(" ", Enumerable.Range(0, 30).Select(i => $"[[P{i}]]")) };
        for (var i = 0; i < 30; i++) pages[$"P{i}.md"] = $"# P{i}\n";
        for (var i = 0; i < 10; i++) pages[$"Alone{i}.md"] = $"# Alone{i}\n";

        var view = GraphViewBuilder.Build(await SnapshotAsync(pages), new GraphViewOptions(MaxNodes: 31));

        Assert.True(view.Truncated);
        Assert.Equal(41, view.TotalPages);
        Assert.Equal(31, view.Nodes.Count);
        Assert.Contains(view.Nodes, n => n.Path == "Hub.md");
        Assert.DoesNotContain(view.Nodes, n => n.Path!.StartsWith("Alone"));
    }

    [Theory]
    [InlineData("Install#Steps", "Install")]
    [InlineData("Roadmap|the plan", "Roadmap")]
    [InlineData("notes/Idea.md", "notes/Idea")]
    [InlineData("./My%20Page.md#top", "My Page")]
    [InlineData("#only-a-heading", null)]
    [InlineData("  ", null)]
    public void MissingName_StripsHeadingsAliasesAndExtensions(string raw, string? expected) =>
        Assert.Equal(expected, GraphViewBuilder.MissingName(raw));

    [Fact]
    public async Task Layout_IsDeterministic_AndSpreadsNodesOut()
    {
        var snapshot = await SnapshotAsync(Sample());
        var a = GraphViewBuilder.Build(snapshot, new GraphViewOptions());
        var b = GraphViewBuilder.Build(snapshot, new GraphViewOptions());

        Assert.Equal(a.Nodes.Select(n => (n.X, n.Y)), b.Nodes.Select(n => (n.X, n.Y)));
        Assert.All(a.Nodes, n => Assert.True(double.IsFinite(n.X) && double.IsFinite(n.Y)));
        foreach (var n in a.Nodes)
            foreach (var m in a.Nodes.Where(m => m != n))
                Assert.True(Math.Abs(n.X - m.X) + Math.Abs(n.Y - m.Y) > 10, $"{n.Id} and {m.Id} overlap");
    }

    // The 400-node layout timing test is in KnowledgePerformanceTests, which runs without parallel tests.

    [Fact]
    public async Task HiddenFolders_DropTheirPages_ButStayListed()
    {
        var snapshot = await SnapshotAsync(Sample());
        var view = GraphViewBuilder.Build(snapshot, new GraphViewOptions(HiddenFolders: ["docs"]));

        Assert.Equal(["Lonely.md", "Welcome.md"], view.Nodes.Where(n => !n.Missing).Select(n => n.Path));
        // Roadmap was only linked from a hidden page; Ideas is linked from Welcome.
        Assert.Equal(["Ideas"], view.Nodes.Where(n => n.Missing).Select(n => n.Label));
        Assert.DoesNotContain(view.Edges, e => e.From.StartsWith("Docs/") || e.To.StartsWith("Docs/"));
        Assert.Equal([new GraphFolder("", 2), new GraphFolder("Docs", 2)], view.Folders);
    }

    [Fact]
    public async Task HiddenFolders_KeepTheCenterPage()
    {
        var snapshot = await SnapshotAsync(Sample());
        var view = GraphViewBuilder.Build(snapshot, new GraphViewOptions("Docs/Install.md", Depth: 1, IncludeMissing: false, HiddenFolders: ["Docs"]));

        Assert.Equal(["Docs/Install.md", "Welcome.md"], view.Nodes.Select(n => n.Path));
        Assert.Equal([new GraphFolder("", 1), new GraphFolder("Docs", 2)], view.Folders);
    }

    [Fact]
    public async Task Search_RanksTitlesAbovePathsAboveText_ThenByLinks()
    {
        var snapshot = await SnapshotAsync(new()
        {
            ["Links.md"] = "# Links\n\nSee [[Hub]] and [[Page]].",
            ["Notes/Blinking.md"] = "# Blinking\n\nSee [[Hub]].",
            ["Hub.md"] = "# Hub\n\nSee [[Links]], [[Blinking]], [[Linkage]] and [[Other]].",
            ["Linkage.md"] = "# Linkage\n\nSee [[Hub]] and [[Links]].",
            ["Other.md"] = "# Other\n\nThe text mentions links once.",
            ["Linker/Page.md"] = "# Page\n\nNothing here.",
        });
        var view = GraphViewBuilder.Build(snapshot, new GraphViewOptions());

        var hits = GraphSearch.Find(view, snapshot, " link ").Select(n => n.Label).ToList();

        // Title starts with "link" (more links first), title contains it, path contains it, text contains it.
        Assert.Equal(["Links", "Linkage", "Blinking", "Page", "Hub", "Other"], hits);
        Assert.Empty(GraphSearch.Find(view, snapshot, "   "));
    }
}
