using System.Globalization;
using System.Text.RegularExpressions;

namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>
/// The application log (AIStoryBuildersLog.csv on MAUI, asb2:log in the browser), newest line first.
/// <see cref="WriteToLog"/> keeps V1's sync signature (D25): it prepends to an in-memory copy and schedules a
/// debounced save through <see cref="IAppDataStore"/>. V2 lines start with an invariant timestamp, which V1 still
/// reads as one text line (UI-Behavior-Spec §7).
/// </summary>
public sealed partial class LogService(IAppDataStore store)
{
    /// <summary>V1 keeps the 1000 newest old lines plus the new one.</summary>
    public const int MaxLines = 1001;

    readonly object _gate = new();
    readonly SemaphoreSlim _saveLock = new(1, 1);
    List<string> _lines = [];
    bool _saveScheduled;

    /// <summary>How long writes are batched before a save. Tests shorten it.</summary>
    public TimeSpan SaveDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Raised after the in-memory log changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when a save fails, for example with a full browser storage.</summary>
    public event Action<Exception>? SaveFailed;

    /// <summary>Loads the stored log. Lines written before this call stay on top.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var stored = StoryFormat.SplitLinesLf(await store.ReadAsync(AppDataFile.Log, ct));
        lock (_gate)
        {
            _lines = [.. _lines, .. stored];
            Cap();
        }
        Changed?.Invoke();
    }

    /// <summary>A snapshot of the log, newest first.</summary>
    public IReadOnlyList<string> Lines
    {
        get { lock (_gate) return [.. _lines]; }
    }

    public void WriteToLog(string logText)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " "
                   + (logText ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        bool startSave;
        lock (_gate)
        {
            _lines.Insert(0, line);
            Cap();
            startSave = !_saveScheduled;
            _saveScheduled = true;
        }
        Changed?.Invoke();
        if (startSave) _ = SaveLaterAsync();
    }

    /// <summary>V1's Clear Log: replaces the log with one "Log cleared on" line.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        lock (_gate) _lines = ["Log cleared on " + DateTime.Now];
        await FlushAsync(ct);
        Changed?.Invoke();
    }

    /// <summary>Saves now.</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _saveLock.WaitAsync(ct);
        try
        {
            string content;
            lock (_gate) content = string.Concat(_lines.Select(l => l + StoryFormat.NewLine));
            await store.WriteAsync(AppDataFile.Log, content, ct);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>The leading V2 timestamp of a log line, or null for V1 lines without one.</summary>
    public static (string? Date, string Entry) SplitTimestamp(string line)
    {
        var m = TimestampRegex().Match(line);
        return m.Success ? (m.Groups[1].Value, line[m.Length..]) : (null, line);
    }

    async Task SaveLaterAsync()
    {
        try
        {
            await Task.Delay(SaveDelay);
        }
        finally
        {
            lock (_gate) _saveScheduled = false;
        }
        try
        {
            await FlushAsync();
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke(ex);
        }
    }

    void Cap()
    {
        if (_lines.Count > MaxLines) _lines.RemoveRange(MaxLines, _lines.Count - MaxLines);
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) ")]
    private static partial Regex TimestampRegex();
}
