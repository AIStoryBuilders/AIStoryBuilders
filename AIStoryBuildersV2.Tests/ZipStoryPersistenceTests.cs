using System.IO.Compression;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;

namespace AIStoryBuildersV2.Tests;

/// <summary>Plan §10: WASM persistence over an in-memory key-value store and a temp dir standing in for MEMFS.</summary>
public class ZipStoryPersistenceTests : IDisposable
{
    const string Title = FixtureFactory.Title;
    readonly TempDir _memfs = new();
    readonly InMemoryKeyValueStore _kv = new();
    readonly TestStoryIndexLock _indexLock = new();
    readonly LocalStorageStoryLibraryStore _library;
    readonly ZipStoryPersistence _p;
    readonly List<StoryPersistenceEvent> _events = [];
    DateTimeOffset _clock = new(2026, 11, 4, 17, 20, 0, TimeSpan.Zero);

    public ZipStoryPersistenceTests()
    {
        _library = new LocalStorageStoryLibraryStore(_kv, _indexLock);
        _p = new ZipStoryPersistence(_kv, new TestHost(_memfs.Path), _library, () => _clock) { SaveDelay = TimeSpan.FromMilliseconds(50) };
        _p.Changed += _events.Add;
    }

    public void Dispose() => _memfs.Dispose();

    string StoryDir => _memfs.Combine(Title);

    async Task SeedSavedStoryAsync()
    {
        FixtureFactory.WriteBefore(_memfs.Path);
        File.Delete(_memfs.Combine("AIStoryBuildersStories.csv"));
        await _library.UpsertAsync(new StoryIndexEntry("0", Title, "Fantasy", "", "", "", null));
        _p.MarkDirty(Title);
        await _p.FlushAsync(Title);
        _events.Clear();
        _kv.WriteLog.Clear();
    }

    async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task Save_zips_the_folder_without_manifest_and_stamps_the_index()
    {
        FixtureFactory.WriteBefore(_memfs.Path);
        File.WriteAllText(Path.Combine(StoryDir, "Manifest.config"), "{}");
        await _library.UpsertAsync(new StoryIndexEntry("0", Title, "Fantasy", "", "", "", null));
        _p.MarkDirty(Title);
        await _p.FlushAsync(Title);

        using var zip = new ZipArchive(new MemoryStream(Convert.FromBase64String(_kv.Items["asb2:story:" + Title])));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("Chapters/Chapter1/Paragraph1.txt", names);
        Assert.Contains("Timelines.csv", names);
        Assert.DoesNotContain("Manifest.config", names);
        Assert.Equal(_clock, (await _library.ListAsync()).Single().SavedAt);
        Assert.False(_p.IsDirty(Title));
        Assert.Equal(StoryPersistenceEventKind.Saved, Assert.Single(_events).Kind);
    }

    [Fact]
    public async Task Open_extracts_the_zip_into_memfs()
    {
        await SeedSavedStoryAsync();
        Directory.Delete(StoryDir, recursive: true);
        await _p.OpenAsync(Title);
        var expected = FixtureFactory.BeforeFiles()[Title + "/Chapters/Chapter2/Paragraph2.txt"];
        Assert.Equal(expected, File.ReadAllText(Path.Combine(StoryDir, "Chapters", "Chapter2", "Paragraph2.txt")));
        Assert.Equal(Title, _p.OpenTitle);
    }

    [Fact]
    public async Task Debounce_merges_ten_mark_dirty_calls_into_one_write()
    {
        await SeedSavedStoryAsync();
        for (var i = 0; i < 10; i++) _p.MarkDirty(Title);
        await WaitUntilAsync(() => !_p.IsDirty(Title));
        Assert.Single(_kv.WriteLog, w => w == "set asb2:story:" + Title);
    }

    [Fact]
    public async Task Quota_failure_raises_save_failed_and_keeps_the_story_dirty()
    {
        await SeedSavedStoryAsync();
        _kv.QuotaBytes = 1000;
        File.AppendAllText(Path.Combine(StoryDir, "Timelines.csv"), "more\r\n");
        _p.MarkDirty(Title);
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => _p.FlushAsync(Title));
        Assert.True(_p.IsDirty(Title));
        Assert.Equal(StoryPersistenceEventKind.SaveFailed, Assert.Single(_events).Kind);
    }

    [Fact]
    public async Task A_details_edit_that_hits_the_quota_fails_the_save_and_survives_a_retry()
    {
        await SeedSavedStoryAsync();
        var entry = (await _library.ListAsync()).Single();
        var used = _kv.Items.Sum(kv => (long)(kv.Key.Length + kv.Value.Length) * 2);
        _kv.QuotaBytes = used + 100; // The zip and the old index entry still fit; the longer synopsis doesn't.
        var synopsis = new string('s', 500);

        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => _library.UpsertAsync(entry with { Synopsis = synopsis }));
        _p.MarkDirty(Title);
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => _p.FlushAsync(Title));

        Assert.True(_p.IsDirty(Title));
        Assert.Equal(StoryPersistenceEventKind.SaveFailed, Assert.Single(_events).Kind);

        _kv.QuotaBytes = null;
        await _p.FlushAsync(Title);

        Assert.False(_p.IsDirty(Title));
        var stored = new LocalStorageStoryLibraryStore(_kv, _indexLock); // What a reload reads.
        Assert.Equal(synopsis, (await stored.ListAsync()).Single().Synopsis);
    }

    [Fact]
    public async Task Reopening_a_dirty_story_keeps_the_unsaved_memfs_copy()
    {
        await SeedSavedStoryAsync();
        await _p.OpenAsync(Title);
        _kv.QuotaBytes = 1000;
        File.WriteAllText(Path.Combine(StoryDir, "unsaved.txt"), "keep me");
        _p.MarkDirty(Title);
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => _p.FlushAsync(Title));

        await _p.OpenAsync(Title);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(StoryDir, "unsaved.txt")));
    }

    [Fact]
    public async Task Opening_another_story_is_blocked_when_the_flush_fails()
    {
        await SeedSavedStoryAsync();
        await _p.OpenAsync(Title);
        _kv.QuotaBytes = 1000;
        _p.MarkDirty(Title);
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => _p.OpenAsync("Other"));
        Assert.Equal(Title, _p.OpenTitle);
        Assert.True(Directory.Exists(StoryDir));
    }

    [Fact]
    public async Task A_missing_zip_is_reported()
    {
        await _library.UpsertAsync(new StoryIndexEntry("0", "Ghost", "", "", "", "", null));
        var ex = await Assert.ThrowsAsync<StoryDataMissingException>(() => _p.OpenAsync("Ghost"));
        Assert.Equal("Story data is missing from browser storage", ex.Message);
    }

    [Fact]
    public async Task A_corrupt_zip_is_reported_and_memfs_is_not_overwritten()
    {
        await SeedSavedStoryAsync();
        await _kv.SetAsync("asb2:story:" + Title, "bm90IGEgemlw");
        await Assert.ThrowsAsync<StoryDataMissingException>(() => _p.OpenAsync(Title));
        Assert.True(File.Exists(Path.Combine(StoryDir, "Timelines.csv")));
    }

    [Fact]
    public async Task A_zip_slip_entry_is_rejected()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("../evil.txt").Open());
            w.Write("x");
        }
        await _kv.SetAsync("asb2:story:Evil", Convert.ToBase64String(buffer.ToArray()));
        await Assert.ThrowsAsync<StoryDataMissingException>(() => _p.OpenAsync("Evil"));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_memfs.Path)!, "evil.txt")));
    }

    [Fact]
    public async Task An_external_change_raises_changed_elsewhere_but_our_own_save_does_not()
    {
        await SeedSavedStoryAsync();
        await _p.OpenAsync(Title);

        _clock = _clock.AddMinutes(1);
        _p.MarkDirty(Title);
        await _p.FlushAsync(Title);
        await _p.OnExternalChangeAsync(StoryStorageConstants.StoriesIndexKey);
        Assert.DoesNotContain(_events, e => e.Kind == StoryPersistenceEventKind.ChangedElsewhere);

        var entry = (await _library.ListAsync()).Single();
        await _library.UpsertAsync(entry with { SavedAt = _clock.AddMinutes(5) });
        await _p.OnExternalChangeAsync("asb2:story:" + Title);
        Assert.Contains(_events, e => e.Kind == StoryPersistenceEventKind.ChangedElsewhere && e.Title == Title);
    }

    [Fact]
    public async Task Reload_drops_the_dirty_copy()
    {
        await SeedSavedStoryAsync();
        await _p.OpenAsync(Title);
        File.WriteAllText(Path.Combine(StoryDir, "unsaved.txt"), "x");
        _kv.QuotaBytes = 1000;
        _p.MarkDirty(Title);
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => _p.FlushAsync(Title));
        await _p.ReloadAsync(Title);
        Assert.False(_p.IsDirty(Title));
        Assert.False(File.Exists(Path.Combine(StoryDir, "unsaved.txt")));
    }

    [Fact]
    public async Task Usage_counts_utf16_bytes_of_asb2_keys()
    {
        await _kv.SetAsync("asb2:story:A", "12345");
        await _kv.SetAsync("asb2:log", "x");
        await _kv.SetAsync("other", "ignored");
        var usage = await _p.GetUsageAsync();
        Assert.Equal(("asb2:story:A".Length + 5 + "asb2:log".Length + 1) * 2, usage!.EstimatedBytes);
        Assert.Equal(1, usage.Stories);
        Assert.Equal(5_000_000, usage.AssumedQuotaBytes);
    }

    [Fact]
    public async Task Delete_removes_the_zip_key()
    {
        await SeedSavedStoryAsync();
        await _p.DeleteAsync(Title);
        Assert.False(_kv.Items.ContainsKey("asb2:story:" + Title));
    }

    [Theory]
    [InlineData("Chapters/Chapter1/Chapter1.txt", "Chapters/Chapter1/Chapter1.txt")]
    [InlineData("Chapters\\Chapter1\\Chapter1.txt", "Chapters/Chapter1/Chapter1.txt")]
    [InlineData("./a/./b.txt", "a/b.txt")]
    [InlineData("../evil.txt", null)]
    [InlineData("a/../../evil.txt", null)]
    [InlineData("/etc/passwd", null)]
    [InlineData("C:/x.txt", null)]
    public void Entry_names_are_normalized_or_rejected(string name, string? expected) =>
        Assert.Equal(expected, SafeZip.NormalizeEntryName(name));
}
