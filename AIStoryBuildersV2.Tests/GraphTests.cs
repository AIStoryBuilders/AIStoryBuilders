using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Models;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>T8: story graph build, persistence, cache and context.</summary>
public class GraphTests : IDisposable
{
    readonly CultureScope _culture = new("en-US");
    readonly ServiceFixture _f = new();

    public void Dispose()
    {
        _f.Dispose();
        _culture.Dispose();
    }

    string GraphFile(string name) => Path.Combine(_f.StoryDir, "Graph", name);

    [Fact]
    public async Task Reload_writes_v1_graph_files_matching_the_golden_copy()
    {
        var story = (await _f.Service.GetStorysAsync()).Single();
        var progress = new List<string>();
        await _f.Service.ReloadStoryAsync(story, new Progress<string>(progress.Add));

        var golden = FixtureFactory.SourceFixturePath("V1Story/graph")!;
        if (Environment.GetEnvironmentVariable("ASB_REGENERATE_FIXTURES") == "1" || !Directory.Exists(golden))
        {
            Directory.CreateDirectory(golden);
            File.Copy(GraphFile("graph.json"), Path.Combine(golden, "graph.json"), overwrite: true);
            File.Copy(GraphFile("metadata.json"), Path.Combine(golden, "metadata.json"), overwrite: true);
        }

        Assert.Equal(File.ReadAllText(Path.Combine(golden, "graph.json")), File.ReadAllText(GraphFile("graph.json")));
        Assert.Equal(File.ReadAllText(Path.Combine(golden, "metadata.json")), File.ReadAllText(GraphFile("metadata.json")));

        // V1's schema: camelCase, CRLF, escaped non-ASCII and HTML characters, no trailing newline.
        var graph = File.ReadAllText(GraphFile("graph.json"));
        Assert.StartsWith("{\r\n  \"storyTitle\": \"Forgetting Baldur\",\r\n  \"nodes\": [", graph);
        Assert.Contains("\"type\": \"character\"", graph);
        Assert.Contains("\\u0027", graph);
        Assert.EndsWith("}", graph);
        var manifest = File.ReadAllText(GraphFile("manifest.json"));
        Assert.Contains("\"version\": \"03.62.00\"", manifest);
        Assert.Contains("\"nodeCount\":", manifest);

        Assert.False(_f.GraphCache.Get(story.Title).IsDirty);
        Assert.NotNull(_f.GraphCache.Get(story.Title).Current);
        Assert.Contains("Re-load complete!", progress);
    }

    [Fact]
    public async Task Reload_with_a_progress_dialog_raises_no_step_toasts()
    {
        var story = (await _f.Service.GetStorysAsync()).Single();
        var toasts = new List<string>();
        _f.Service.TextEvent += (_, e) => toasts.Add(e.Message);

        await _f.Service.ReloadStoryAsync(story, new Progress<string>(_ => { }));
        Assert.Empty(toasts);

        await _f.Service.ReloadStoryAsync(story);
        Assert.Contains("Reloading story data…", toasts);
    }

    [Fact]
    public async Task EnsureGraphExists_builds_once_then_loads_from_disk_and_rebuilds_when_dirty()
    {
        await _f.Service.EnsureGraphExistsAsync(FixtureFactory.Title);
        Assert.True(File.Exists(GraphFile("graph.json")));
        var first = _f.GraphCache.Get(FixtureFactory.Title).Current;

        await _f.Service.EnsureGraphExistsAsync(FixtureFactory.Title);
        Assert.Same(first, _f.GraphCache.Get(FixtureFactory.Title).Current);

        _f.Service.AddTimeline(new Timeline { Story = _f.Story, TimelineName = "Later", TimelineDescription = "", StartDate = new DateTime(2026, 12, 1) });
        Assert.True(_f.GraphCache.Get(FixtureFactory.Title).IsDirty);
        await _f.Service.EnsureGraphExistsAsync(FixtureFactory.Title);
        Assert.Contains(_f.GraphCache.Get(FixtureFactory.Title).Current!.Nodes, n => n.Label == "Later");
    }

    [Fact]
    public void Two_stories_do_not_share_graphs()
    {
        var cache = new StoryGraphCache();
        cache.Get("A").Current = new StoryGraph { StoryTitle = "A" };
        cache.Get("B").Current = new StoryGraph { StoryTitle = "B" };
        Assert.Equal("A", cache.Get("a").Current!.StoryTitle);
        Assert.Equal("B", cache.Get("B").Current!.StoryTitle);
        cache.Remove("A");
        Assert.Null(cache.Get("A").Current);
    }

    [Fact]
    public async Task Concurrent_flows_each_see_their_own_story()
    {
        var cache = new StoryGraphCache();
        cache.Get("A").CurrentStory = new Story { Title = "A", Style = "Fantasy" };
        cache.Get("B").CurrentStory = new Story { Title = "B", Style = "Mystery" };
        var query = new GraphQueryService(cache);

        async Task<string> Flow(string title)
        {
            using var _ = StoryGraphContext.Use(title);
            await Task.Delay(20);
            await Task.Yield();
            return query.GetStoryStyle();
        }

        var results = await Task.WhenAll(Flow("A"), Flow("B"), Flow("A"));
        Assert.Equal(["Fantasy", "Mystery", "Fantasy"], results);
        Assert.Null(StoryGraphContext.Current);
    }

    [Fact]
    public async Task Without_a_context_the_services_refuse_to_guess()
    {
        var query = new GraphQueryService(new StoryGraphCache());
        var ex = Assert.Throws<InvalidOperationException>(() => query.GetStoryStyle());
        Assert.Equal("No story in StoryGraphContext", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_context_flows_into_a_tool_called_by_FunctionInvokingChatClient()
    {
        var cache = new StoryGraphCache();
        cache.Get("Forgetting Baldur").CurrentStory = new Story { Title = "Forgetting Baldur", Style = "Fantasy" };
        var query = new GraphQueryService(cache);
        var tool = AIFunctionFactory.Create(() => query.GetStoryStyle(), "get_story_style");

        var scripted = new ScriptedChatClient();
        scripted.EnqueueCalls(ScriptedChatClient.Call("c1", "get_story_style")).EnqueueText("done");
        var client = new ChatClientBuilder(scripted).UseFunctionInvocation().Build();

        using (StoryGraphContext.Use("Forgetting Baldur"))
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "style?")], new ChatOptions { Tools = [tool] });
        }

        var result = scripted.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains("Fantasy", result.Result?.ToString());
    }

    [Fact]
    public void Timeline_summary_caps_all_sections_and_reserves_an_omission_marker()
    {
        var context = new TimelineContextDto
        {
            TimelineName = "Long timeline",
            TimelineDescription = string.Join(" ", Enumerable.Repeat("description", 350)),
            Characters =
            [
                new TimelineCharacterDto
                {
                    Name = "Character",
                    Attributes = [string.Join(" ", Enumerable.Repeat("attribute", 300))]
                }
            ],
            Locations =
            [
                new TimelineLocationDto
                {
                    Name = "Location",
                    Description = string.Join(" ", Enumerable.Repeat("location", 300))
                }
            ]
        };

        var summary = new TimelineSummaryGenerator().GenerateSummary(context);

        Assert.InRange(summary.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length, 1, 800);
        Assert.Contains("earlier timeline details omitted", summary);
    }

    [Fact]
    public void Timeline_summary_keeps_recent_events_without_exceeding_the_word_limit()
    {
        var context = new TimelineContextDto
        {
            TimelineName = "Timeline",
            Events = Enumerable.Range(1, 300).Select(i => new TimelineEventDto
            {
                Chapter = $"Chapter {i}",
                ParagraphIndex = i
            }).ToList()
        };

        var summary = new TimelineSummaryGenerator().GenerateSummary(context);

        Assert.InRange(summary.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length, 1, 800);
        Assert.Contains("- Chapter 300, P300:", summary);
        Assert.DoesNotContain("- Chapter 1, P1:", summary);
    }
}
