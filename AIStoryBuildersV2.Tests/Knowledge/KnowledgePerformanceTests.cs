using System.Diagnostics;
using AIStoryBuildersV2.Shared.Knowledge;
using Xunit.Abstractions;

namespace AIStoryBuildersV2.Tests;

/// <summary>
/// Timing tests run in their own collection, after every parallel test has finished, so other tests don't compete for the CPU.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}

[Collection(TimingCollection.Name)]
public sealed class KnowledgePerformanceTests(ITestOutputHelper output)
{
    /// <summary>
    /// Runs <paramref name="run"/> up to <paramref name="attempts"/> times and stops at the first run within
    /// <paramref name="budget"/>. One slow run (a GC pause, a busy machine) can't fail the test; a real slowdown fails every run.
    /// </summary>
    async Task AssertWithinAsync(string what, TimeSpan budget, Func<Task> run, int attempts = 3)
    {
        var times = new List<TimeSpan>();
        for (var i = 0; i < attempts; i++)
        {
            var sw = Stopwatch.StartNew();
            await run();
            sw.Stop();
            times.Add(sw.Elapsed);
            output.WriteLine($"{what}, run {i + 1}: {sw.Elapsed.TotalMilliseconds:N0} ms");
            if (sw.Elapsed < budget) return;
        }
        Assert.Fail($"{what} took {string.Join(", ", times.Select(t => $"{t.TotalMilliseconds:N0} ms"))}; the budget is {budget.TotalMilliseconds:N0} ms");
    }

    [Fact]
    public async Task Rebuild_FiveThousandPages_UnderTwoSeconds()
    {
        // Measured over an in-memory store so disk and antivirus speed don't decide the result.
        var pages = new Dictionary<string, string>();
        for (var f = 0; f < 50; f++)
            for (var i = 0; i < 100; i++)
                pages[$"F{f}/Page {i}.md"] =
                    $"---\ntitle: Page {f}-{i}\ntags: [t{i % 7}]\n---\n# Page {f}-{i}\n\nLinks to [[Page {(i + 1) % 100}]] and [[F{(f + 1) % 50}/Page {i}]] and [[Missing {i}]].\n";
        await new KnowledgeGraphService(new GraphAndWorkspaceTests.MemoryStore(pages.Take(50).ToDictionary())).RebuildAsync(); // warm up

        KnowledgeGraphService graph = null!;
        GraphStats stats = default!;
        await AssertWithinAsync("Rebuild of 5,000 pages", TimeSpan.FromSeconds(2), async () =>
        {
            graph = new KnowledgeGraphService(new GraphAndWorkspaceTests.MemoryStore(pages));
            stats = await graph.RebuildAsync();
        });
        Assert.Equal(5000, stats.Pages);
        Assert.Equal(15000, stats.Links);
        Assert.Equal(5000, stats.Unresolved);

        // Updating one page re-resolves the graph without a full re-read. The same update again changes nothing.
        await AssertWithinAsync("Update of one page", TimeSpan.FromSeconds(2),
            () => graph.UpdatePageAsync("F0/Page 0.md", "# Changed\n[[Missing 0]]", 2));
        Assert.Equal(14998, (await graph.GetStatsAsync()).Links);
    }

    [Fact]
    public async Task Layout_OfFourHundredNodes_IsQuick()
    {
        var pages = new Dictionary<string, string>();
        for (var i = 0; i < 400; i++) pages[$"F{i % 8}/P{i}.md"] = $"# P{i}\n[[P{(i + 1) % 400}]] [[P{(i * 7) % 400}]]";
        var graph = new KnowledgeGraphService(new GraphAndWorkspaceTests.MemoryStore(pages));
        await graph.RebuildAsync();

        GraphView view = null!;
        await AssertWithinAsync("Layout of 400 nodes", TimeSpan.FromSeconds(3), () =>
        {
            view = GraphViewBuilder.Build(graph.Snapshot, new GraphViewOptions());
            return Task.CompletedTask;
        });

        Assert.Equal(400, view.Nodes.Count);
        var (_, _, width, height) = view.Bounds();
        Assert.InRange(width, 300, 20000);
        Assert.InRange(height, 300, 20000);
    }
}
