namespace AIStoryBuildersV2.Tests;

public class V1TreeComparerTests : IDisposable
{
    readonly TempDir _a = new(), _b = new();

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
    }

    [Fact]
    public void Before_matches_itself()
    {
        var before = FixtureFactory.SourceFixturePath("V1Story/before")!;
        Assert.Empty(V1TreeComparer.Compare(before, before));
    }

    [Fact]
    public void Different_vectors_with_valid_format_match_but_text_changes_do_not()
    {
        FixtureFactory.WriteBefore(_a.Path);
        FixtureFactory.WriteBefore(_b.Path);
        var p = Path.Combine(_b.Path, FixtureFactory.Title, "Chapters", "Chapter1", "Paragraph1.txt");
        File.WriteAllText(p, "Ridge|Years Before|[Balt,Father]|Balt and his father held the ridge until dusk.|" + FixtureVectors.Format("other"));
        Assert.Empty(V1TreeComparer.Compare(_a.Path, _b.Path));

        File.WriteAllText(p, "Ridge|Years Before|[Balt,Father]|Changed.|" + FixtureVectors.Format("other"));
        Assert.Contains(V1TreeComparer.Compare(_a.Path, _b.Path), d => d.StartsWith("differs: Chapters/Chapter1/Paragraph1.txt"));
    }

    [Fact]
    public void Graph_is_ignored_and_ids_in_the_stories_row_are_ignored()
    {
        FixtureFactory.WriteBefore(_a.Path);
        FixtureFactory.WriteBefore(_b.Path);
        Directory.CreateDirectory(Path.Combine(_b.Path, FixtureFactory.Title, "Graph"));
        File.WriteAllText(Path.Combine(_b.Path, FixtureFactory.Title, "Graph", "graph.json"), "{}");
        var csv = Path.Combine(_b.Path, "AIStoryBuildersStories.csv");
        File.WriteAllText(csv, File.ReadAllText(csv).Replace("1|Forgetting", "7|Forgetting"));
        Assert.Empty(V1TreeComparer.Compare(_a.Path, _b.Path));
    }

    [Fact]
    public void A_missing_file_and_a_bom_are_reported()
    {
        FixtureFactory.WriteBefore(_a.Path);
        FixtureFactory.WriteBefore(_b.Path);
        File.Delete(Path.Combine(_b.Path, FixtureFactory.Title, "Locations", "Ridge.csv"));
        var t = Path.Combine(_b.Path, FixtureFactory.Title, "Timelines.csv");
        File.WriteAllText(t, File.ReadAllText(t), new System.Text.UTF8Encoding(true));
        var d = V1TreeComparer.Compare(_a.Path, _b.Path);
        Assert.Contains("missing: Locations/Ridge.csv", d);
        Assert.Contains(d, x => x.StartsWith("actual has a BOM"));
    }
}
