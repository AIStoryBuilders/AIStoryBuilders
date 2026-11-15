using AIStoryBuildersV2.Shared.Hosting;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;

namespace AIStoryBuildersV2.Tests;

public class CsvStoryLibraryStoreTests : IDisposable
{
    readonly TempDir _dir = new();
    CsvStoryLibraryStore Store => new(_dir.Path);
    string FileText => File.ReadAllText(Path.Combine(_dir.Path, "AIStoryBuildersStories.csv"));

    public void Dispose() => _dir.Dispose();

    static StoryIndexEntry Entry(string title, string synopsis = "S", string worldFacts = "W") =>
        new("0", title, "Fantasy", "Theme", synopsis, worldFacts, null);

    [Fact]
    public async Task Upsert_appends_rows_with_v1_ids_and_crlf()
    {
        await Store.UpsertAsync(Entry("A"));
        await Store.UpsertAsync(Entry("B"));
        Assert.Equal("1|A|Fantasy|Theme|S|W\r\n2|B|Fantasy|Theme|S|W\r\n", FileText);
    }

    [Fact]
    public async Task Upsert_of_an_existing_title_moves_it_to_the_end_ignoring_case()
    {
        await Store.UpsertAsync(Entry("A"));
        await Store.UpsertAsync(Entry("B"));
        await Store.UpsertAsync(Entry("a", synopsis: "New"));
        var rows = await Store.ListAsync();
        Assert.Equal(["B", "a"], rows.Select(r => r.Title));
        Assert.Equal("2", rows[1].Id);
        Assert.Equal("New", rows[1].Synopsis);
    }

    [Fact]
    public async Task Free_text_loses_pipes_and_line_breaks()
    {
        await Store.UpsertAsync(Entry("A", synopsis: "One|two\r\nthree", worldFacts: "Fact 1\nFact 2"));
        Assert.Equal("1|A|Fantasy|Theme|Onetwo three|Fact 1 Fact 2\r\n", FileText);
    }

    [Fact]
    public async Task Remove_ignores_case()
    {
        await Store.UpsertAsync(Entry("Alpha"));
        await Store.UpsertAsync(Entry("Beta"));
        await Store.RemoveAsync("ALPHA");
        Assert.Equal(["Beta"], (await Store.ListAsync()).Select(r => r.Title));
    }

    [Fact]
    public async Task Reads_v1_rows_with_and_without_world_facts()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "AIStoryBuildersStories.csv"),
            "\r\n1|Old|Drama|T|Syn\r\n2|New|Drama|T|Syn|Facts\r\nnot a row\r\n");
        var rows = await Store.ListAsync();
        Assert.Equal(["Old", "New"], rows.Select(r => r.Title));
        Assert.Equal(["", "Facts"], rows.Select(r => r.WorldFacts));
    }
}

public class LocalStorageStoryLibraryStoreTests
{
    readonly InMemoryKeyValueStore _kv = new();
    readonly RecordingStoryIndexLock _indexLock = new();
    LocalStorageStoryLibraryStore Store => new(_kv, _indexLock);

    [Fact]
    public async Task Upsert_writes_a_camel_case_index()
    {
        await Store.UpsertAsync(new StoryIndexEntry("0", "Forgetting Baldur", "Fantasy", "T", "S", "W", new DateTimeOffset(2026, 11, 4, 17, 20, 0, TimeSpan.Zero)));
        var json = _kv.Items[StoryStorageConstants.StoriesIndexKey];
        Assert.StartsWith("{\"version\":1,\"stories\":[{\"id\":\"1\",\"title\":\"Forgetting Baldur\"", json);
        Assert.Contains("\"savedAt\":\"2026-11-04T17:20:00+00:00\"", json);
    }

    [Fact]
    public async Task Upsert_replaces_by_title_ignoring_case_and_remove_works()
    {
        await Store.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", "1", "", null));
        await Store.UpsertAsync(new StoryIndexEntry("0", "B", "S", "T", "1", "", null));
        await Store.UpsertAsync(new StoryIndexEntry("0", "a", "S", "T", "2", "", null));
        var rows = await Store.ListAsync();
        Assert.Equal(["a", "B"], rows.Select(r => r.Title));
        Assert.Equal("2", rows[0].Synopsis);
        await Store.RemoveAsync("A");
        Assert.Equal(["B"], (await Store.ListAsync()).Select(r => r.Title));
    }

    [Fact]
    public async Task A_corrupt_index_reads_as_empty()
    {
        await _kv.SetAsync(StoryStorageConstants.StoriesIndexKey, "{not json");
        Assert.Empty(await Store.ListAsync());
    }

    [Fact]
    public async Task An_edit_that_hits_the_quota_stays_visible_and_is_written_by_the_next_upsert()
    {
        var saved = new DateTimeOffset(2026, 11, 4, 17, 20, 0, TimeSpan.Zero);
        var store = Store;
        await store.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", "Old", "", saved));
        _kv.QuotaBytes = _kv.Items[StoryStorageConstants.StoriesIndexKey].Length * 2L + StoryStorageConstants.StoriesIndexKey.Length * 2L;

        await Assert.ThrowsAsync<StorageQuotaExceededException>(() =>
            store.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", new string('n', 500), "", saved.AddHours(1))));

        var shown = Assert.Single(await store.ListAsync());
        Assert.Equal(500, shown.Synopsis.Length);
        Assert.Equal(saved, shown.SavedAt); // Another tab's save is recognized by this value.
        Assert.Equal("Old", Assert.Single(await Store.ListAsync()).Synopsis); // Nothing was stored.

        _kv.QuotaBytes = null;
        await store.UpsertAsync(shown with { SavedAt = saved.AddHours(2) });

        Assert.Equal(500, Assert.Single(await Store.ListAsync()).Synopsis.Length);
    }

    [Fact]
    public async Task A_new_story_that_hits_the_quota_does_not_appear_in_the_list()
    {
        var store = Store;
        _kv.QuotaBytes = 10;

        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => store.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", "1", "", null)));

        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Removing_a_story_forgets_its_unsaved_edit()
    {
        var store = Store;
        await store.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", "1", "", null));
        _kv.QuotaBytes = 10;
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => store.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", "2", "", null)));
        _kv.QuotaBytes = null;

        await store.RemoveAsync("A");
        await store.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", "3", "", null));

        Assert.Equal("3", Assert.Single(await store.ListAsync()).Synopsis);
    }

    [Fact]
    public async Task Index_mutations_run_inside_the_shared_lock()
    {
        var store = Store;
        await store.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", "1", "", null));
        await store.RemoveAsync("A");

        Assert.Equal(2, _indexLock.CallCount);
    }

    [Fact]
    public async Task Concurrent_upserts_from_separate_store_instances_do_not_overwrite_each_other()
    {
        var kv = new YieldingKeyValueStore();
        var indexLock = new RecordingStoryIndexLock();
        var first = new LocalStorageStoryLibraryStore(kv, indexLock);
        var second = new LocalStorageStoryLibraryStore(kv, indexLock);

        await Task.WhenAll(
            first.UpsertAsync(new StoryIndexEntry("0", "A", "S", "T", "1", "", null)),
            second.UpsertAsync(new StoryIndexEntry("0", "B", "S", "T", "1", "", null)));

        Assert.Equal(["A", "B"], (await first.ListAsync()).Select(entry => entry.Title));
    }

    sealed class RecordingStoryIndexLock : IStoryIndexLock
    {
        readonly SemaphoreSlim _lock = new(1, 1);

        public int CallCount { get; private set; }

        public async Task RunAsync(Func<Task> action, CancellationToken ct = default)
        {
            CallCount++;
            await _lock.WaitAsync(ct);
            try { await action(); }
            finally { _lock.Release(); }
        }
    }

    sealed class YieldingKeyValueStore : IKeyValueStore
    {
        readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

        public async Task<string?> GetAsync(string key)
        {
            var value = _items.GetValueOrDefault(key);
            await Task.Yield();
            return value;
        }

        public async Task SetAsync(string key, string value)
        {
            await Task.Yield();
            _items[key] = value;
        }

        public Task RemoveAsync(string key)
        {
            _items.Remove(key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> KeysAsync(string prefix) =>
            Task.FromResult<IReadOnlyList<string>>(_items.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList());
    }
}

public class AppDataStoreTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task File_store_uses_v1_file_names()
    {
        var store = new FileAppDataStore(_dir.Path);
        await store.WriteAsync(AppDataFile.Log, "line\r\n");
        Assert.Equal("line\r\n", File.ReadAllText(Path.Combine(_dir.Path, "AIStoryBuildersLog.csv")));
        Assert.Equal("line\r\n", await store.ReadAsync(AppDataFile.Log));
        Assert.Null(await new FileAppDataStore(_dir.Combine("missing")).ReadAsync(AppDataFile.Log));
    }

    [Fact]
    public async Task Local_storage_store_uses_asb2_keys_and_reports_quota()
    {
        var kv = new InMemoryKeyValueStore();
        var store = new LocalStorageAppDataStore(kv);
        await store.WriteAsync(AppDataFile.Log, "x");
        Assert.Equal("x", kv.Items["asb2:log"]);
        kv.QuotaBytes = 10;
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => store.WriteAsync(AppDataFile.Log, new string('x', 100)));
    }
}

public class LogServiceTests
{
    [Fact]
    public async Task Write_prepends_a_timestamped_line_and_flush_saves_crlf()
    {
        var data = new MemoryAppDataStore();
        data.Files[AppDataFile.Log] = "Application started at 11/5/2026 3:04:05 PM\r\n";
        var log = new LogService(data) { SaveDelay = TimeSpan.FromMinutes(10) };
        await log.InitializeAsync();
        log.WriteToLog("Story created\nX");
        await log.FlushAsync();

        var lines = data.Files[AppDataFile.Log].Split("\r\n");
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} Story created X$", lines[0]);
        Assert.Equal("Application started at 11/5/2026 3:04:05 PM", lines[1]);
        Assert.Equal("", lines[2]);
    }

    [Fact]
    public async Task Log_keeps_1000_old_lines_plus_the_new_one()
    {
        var data = new MemoryAppDataStore();
        data.Files[AppDataFile.Log] = string.Concat(Enumerable.Range(0, 1200).Select(i => $"old {i}\r\n"));
        var log = new LogService(data) { SaveDelay = TimeSpan.FromMinutes(10) };
        await log.InitializeAsync();
        log.WriteToLog("new");
        Assert.Equal(1001, log.Lines.Count);
        Assert.Equal("old 999", log.Lines[^1]);
    }

    [Fact]
    public async Task Debounced_save_happens_after_the_delay()
    {
        var data = new MemoryAppDataStore();
        var log = new LogService(data) { SaveDelay = TimeSpan.FromMilliseconds(20) };
        log.WriteToLog("a");
        log.WriteToLog("b");
        for (var i = 0; i < 100 && !data.Files.ContainsKey(AppDataFile.Log); i++) await Task.Delay(20);
        Assert.Contains(" b\r\n", data.Files[AppDataFile.Log]);
    }

    [Theory]
    [InlineData("2026-11-05 15:04:05 Story created", "2026-11-05 15:04:05", "Story created")]
    [InlineData("Application started at 11/5/2026", null, "Application started at 11/5/2026")]
    public void SplitTimestamp(string line, string? date, string entry)
    {
        var (d, e) = LogService.SplitTimestamp(line);
        Assert.Equal(date, d);
        Assert.Equal(entry, e);
    }
}
