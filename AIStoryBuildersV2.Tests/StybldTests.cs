using System.IO.Compression;
using System.Text;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;
using Newtonsoft.Json.Linq;
using Xceed.Words.NET;

namespace AIStoryBuildersV2.Tests;

/// <summary>Plan §10 .stybld: export/import round trip, D6 manifest, and the import refusals (§7.7).</summary>
public class StybldTests : IDisposable
{
    readonly CultureScope _culture = new("en-US");
    readonly ServiceFixture _source = new();
    readonly ServiceFixture _target = new(writeBefore: false);

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
        _culture.Dispose();
    }

    async Task<Story> SourceStoryAsync() => (await _source.Service.GetStorysAsync()).Single();

    void AddKnowledgeBase(string storyDir)
    {
        Directory.CreateDirectory(Path.Combine(storyDir, "KnowledgeBase", "Lore"));
        File.WriteAllText(Path.Combine(storyDir, "KnowledgeBase", "Lore", "Necklace.md"), "---\ntitle: The Necklace\n---\nForged by **Hrafnkel**.\n");
    }

    static Dictionary<string, byte[]> Tree(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllBytes);

    static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        }
        return buffer.ToArray();
    }

    [Fact]
    public async Task Export_then_import_gives_an_identical_tree_including_the_knowledge_base()
    {
        AddKnowledgeBase(_source.StoryDir);
        File.WriteAllText(Path.Combine(_source.StoryDir, "Manifest.config"), "{\"Version\":\"01.00.00\",\"Title\":\"Stale\"}");
        var bytes = _source.Service.ExportProject(await SourceStoryAsync());

        Assert.Equal("The project 'Forgetting Baldur' was successfully imported.", await _target.Service.ImportProjectAsync(bytes));

        var expected = Tree(_source.StoryDir);
        expected.Remove("Manifest.config");
        var actual = Tree(_target.StoryDir);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, data) in expected) Assert.True(data.AsSpan().SequenceEqual(actual[path]), path);

        var row = Assert.Single(await _target.Service.GetStorysAsync());
        Assert.Equal("Ravens carry messages for Odin.", row.WorldFacts);
        Assert.Equal([FixtureFactory.Title], _target.Persistence.Dirty);
    }

    [Fact]
    public async Task The_manifest_has_v1s_version_and_world_facts_and_is_fresh()
    {
        File.WriteAllText(Path.Combine(_source.StoryDir, "Manifest.config"), "{\"Version\":\"01.00.00\",\"Title\":\"Stale\"}");
        using var zip = new ZipArchive(new MemoryStream(_source.Service.ExportProject(await SourceStoryAsync())));
        Assert.Single(zip.Entries, e => e.FullName == "Manifest.config");
        using var reader = new StreamReader(zip.GetEntry("Manifest.config")!.Open());
        var manifest = JObject.Parse(await reader.ReadToEndAsync());
        Assert.Equal("03.62.00", manifest["Version"]!.ToString());
        Assert.Equal("Forgetting Baldur", manifest["Title"]!.ToString());
        Assert.Equal("Ravens carry messages for Odin.", manifest["WorldFacts"]!.ToString());
        Assert.Contains(zip.Entries, e => e.FullName == "Chapters/Chapter1/Paragraph1.txt");
    }

    [Theory]
    [InlineData("03.63.00")]
    [InlineData("4.0")]
    [InlineData("03.62.00.01")]
    [InlineData("x.y")]
    [InlineData(null)]
    public async Task Rejects_newer_or_unreadable_versions(string? version)
    {
        var manifest = version is null ? "{\"Title\":\"New\"}" : $"{{\"Version\":\"{version}\",\"Title\":\"New\"}}";
        var message = await _target.Service.ImportProjectAsync(Zip(("Manifest.config", manifest), ("Timelines.csv", "")));
        Assert.Equal(AIStoryBuildersService.ImportMessages.Incompatible, message);
        Assert.Empty(await _target.Service.GetStorysAsync());
    }

    [Fact]
    public async Task Rejects_an_existing_title_ignoring_case()
    {
        var message = await _source.Service.ImportProjectAsync(Zip(("Manifest.config", "{\"Version\":\"03.62.00\",\"Title\":\"FORGETTING BALDUR\"}")));
        Assert.Equal("The story FORGETTING BALDUR already exists. Backup and delete it before trying to import a new version.", message);
    }

    [Fact]
    public async Task Rejects_a_zip_slip_entry_and_writes_nothing()
    {
        var message = await _target.Service.ImportProjectAsync(Zip(("Manifest.config", "{\"Version\":\"03.62.00\",\"Title\":\"Evil\"}"), ("../evil.txt", "x")));
        Assert.Contains("unsafe path", message);
        Assert.False(Directory.Exists(Path.Combine(_target.Dir.Path, "Evil")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_target.Dir.Path)!, "evil.txt")));
        Assert.Empty(await _target.Service.GetStorysAsync());
    }

    [Fact]
    public async Task Rejects_a_title_that_breaks_the_rules_before_any_path_is_built()
    {
        var message = await _target.Service.ImportProjectAsync(Zip(("Manifest.config", "{\"Version\":\"03.62.00\",\"Title\":\"../x\"}"), ("Timelines.csv", "")));
        Assert.Equal("Story Title cannot contain / \\ : * ? \" < > | or #", message);
        Assert.Empty(Directory.GetDirectories(_target.Dir.Path));
    }

    [Fact]
    public async Task Imports_a_v1_export_without_world_facts()
    {
        // V1 zips the folder with its stale Manifest.config at the root and no WorldFacts.
        var v1 = Zip(
            ("Manifest.config", "{\"Version\":\"03.62.00\",\"Title\":\"Old Tale\",\"Style\":\"Drama\",\"Theme\":\"T\",\"Synopsis\":\"S\",\"ExportedDate\":\"11/5/2026 3:04:05 PM\"}\r\n"),
            ("Timelines.csv", "Now|d|11/5/2026 3:04 PM|\r\n"),
            ("Chapters/Chapter1/Chapter1.txt", "Syn|" + FixtureVectors.Format("Syn")),
            ("Chapters/Chapter1/Paragraph1.txt", "|Now|[]|Text.|" + FixtureVectors.Format("Text.")));
        Assert.Equal("The project 'Old Tale' was successfully imported.", await _target.Service.ImportProjectAsync(v1));
        var story = Assert.Single(await _target.Service.GetStorysAsync());
        Assert.Equal("", story.WorldFacts);
        Assert.False(File.Exists(Path.Combine(_target.Dir.Path, "Old Tale", "Manifest.config")));
        Assert.Single(_target.Service.GetChapters(story));
    }

    [Fact]
    public async Task Wasm_quota_removes_the_partial_story()
    {
        using var memfs = new TempDir();
        var kv = new InMemoryKeyValueStore { QuotaBytes = 2000 };
        var library = new LocalStorageStoryLibraryStore(kv, new TestStoryIndexLock());
        var persistence = new ZipStoryPersistence(kv, new TestHost(memfs.Path), library);
        var log = new LogService(new MemoryAppDataStore());
        var embeddings = new FakeEmbeddingGenerator();
        var service = new AIStoryBuildersService(new TestHost(memfs.Path), library, persistence, log, embeddings, _target.Orchestrator,
            new Shared.Graph.StoryGraphCache(), new Shared.Graph.GraphBuilder(), new Shared.Models.AppMetadata { Version = "03.62.00" });

        var bytes = _source.Service.ExportProject(await SourceStoryAsync());
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => service.ImportProjectAsync(bytes));
        Assert.Empty(await library.ListAsync());
        Assert.False(Directory.Exists(Path.Combine(memfs.Path, FixtureFactory.Title)));
    }

    [Fact]
    public async Task Word_export_has_a_title_and_each_chapter()
    {
        var bytes = _source.Service.ExportWordDocument(await SourceStoryAsync());
        using var doc = DocX.Load(new MemoryStream(bytes));
        var text = string.Join("\n", doc.Paragraphs.Select(p => p.Text));
        Assert.Contains("Forgetting Baldur", text);
        Assert.Contains("Chapter 2", text);
        Assert.Contains("Freya handed Balt a raven feather.", text);
    }
}
