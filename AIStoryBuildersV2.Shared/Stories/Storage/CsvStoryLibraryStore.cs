namespace AIStoryBuildersV2.Shared.Stories.Storage;

/// <summary>
/// MAUI story index: V1's AIStoryBuildersStories.csv, "Id|Title|Style|Theme|Synopsis|WorldFacts" (V1-File-Format §3.1).
/// Writes follow V1: an updated row moves to the end with Id = (other rows) + 1, and free text loses '|' and line breaks (D32).
/// </summary>
public sealed class CsvStoryLibraryStore(string rootPath) : IStoryLibraryStore
{
    readonly SemaphoreSlim _lock = new(1, 1);

    public string FilePath { get; } = Path.Combine(rootPath, "AIStoryBuildersStories.csv");

    public async Task<IReadOnlyList<StoryIndexEntry>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return ReadRows();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(StoryIndexEntry entry, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var rows = ReadRows().Where(r => !StoryTitle.SameTitle(r.Title, entry.Title)).ToList();
            rows.Add(entry with { Id = (rows.Count + 1).ToString() });
            Write(rows);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string title, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var rows = ReadRows();
            var kept = rows.Where(r => !StoryTitle.SameTitle(r.Title, title)).ToList();
            if (kept.Count != rows.Count) Write(kept);
        }
        finally
        {
            _lock.Release();
        }
    }

    List<StoryIndexEntry> ReadRows()
    {
        if (!File.Exists(FilePath)) return [];
        return Parse(File.ReadAllText(FilePath)).ToList();
    }

    /// <summary>V1's filter: field 0 is all digits and there are more than 3 fields. Bad rows are skipped one at a time.</summary>
    public static IEnumerable<StoryIndexEntry> Parse(string text)
    {
        foreach (var line in StoryFormat.SplitLinesLf(text))
        {
            if (line.Trim().Length == 0) continue;
            var f = line.Split('|');
            if (f[0].Length == 0 || !f[0].All(char.IsDigit) || f.Length <= 3) continue;
            yield return new StoryIndexEntry(
                Id: f[0],
                Title: f[1],
                Style: f[2],
                Theme: f[3],
                Synopsis: f.Length > 4 ? f[4] : "",
                WorldFacts: f.Length > 5 ? f[5] : "",
                SavedAt: null);
        }
    }

    void Write(IEnumerable<StoryIndexEntry> rows) =>
        StoryFormat.WriteAllLines(FilePath, rows.Select(r =>
            $"{r.Id}|{r.Title}|{StoryFormat.CleanField(r.Style)}|{StoryFormat.CleanField(r.Theme)}|{StoryFormat.CleanField(r.Synopsis)}|{StoryFormat.CleanField(r.WorldFacts)}"));
}
