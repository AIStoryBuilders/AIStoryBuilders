#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIStoryBuildersV2.Shared.Fingerprint;

namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>
/// The story's lexical fingerprint, stored as {Title}/LexicalFingerprint.json (Lexical-Fingerprint-Plan §6.2, LF1/D33).
/// Reads never throw (LF11); writes end with <see cref="IStoryPersistence.MarkDirty"/> and not Touched, because the
/// fingerprint is not part of the story graph (LF13).
/// </summary>
public partial class AIStoryBuildersService
{
    static readonly JsonSerializerOptions FingerprintJsonOptions = new() { WriteIndented = true, NewLine = StoryFormat.NewLine };

    /// <summary>{BasePath}/{Title}/LexicalFingerprint.json.</summary>
    public string FingerprintPath(string title) => Path.Combine(StoryPath(title), LexicalFingerprintDefaults.FileName);

    /// <summary>
    /// None if the file is missing. Invalid (with Error) if it is unreadable, over 1 MB, or fails §6.1. Never throws.
    /// When <paramref name="logInvalid"/> is true an Invalid result is logged by name and reason, never by content.
    /// </summary>
    public async Task<StoryFingerprint> GetLexicalFingerprintAsync(string title, bool logInvalid = false)
    {
        StoryFingerprint result;
        try
        {
            var path = FingerprintPath(title);
            if (!File.Exists(path)) return StoryFingerprint.NoneValue;

            if (new FileInfo(path).Length > LexicalFingerprintDefaults.MaxFileBytes)
            {
                result = Invalid(null, LexicalFingerprintParser.TooLarge);
            }
            else
            {
                var bytes = await File.ReadAllBytesAsync(path);
                // The stored file name says nothing about the fingerprint, so a bare file gets the default name (§6.2).
                var parsed = LexicalFingerprintParser.Parse(bytes, null);
                result = parsed.Ok
                    ? new StoryFingerprint(FingerprintStatus.Ok, parsed.SuggestedName, parsed.Fingerprint, ReadUploadedUtc(bytes), null)
                    : Invalid(parsed.SuggestedName, parsed.Error);
            }
        }
        catch (Exception)
        {
            result = Invalid(null, LexicalFingerprintParser.Unreadable);
        }

        if (logInvalid && result.Status == FingerprintStatus.Invalid)
        {
            try { LogService.WriteToLog($"Lexical fingerprint for '{title}' is invalid: {result.Error}"); }
            catch (Exception) { }
        }
        return result;

        static StoryFingerprint Invalid(string? name, string? error) => new(FingerprintStatus.Invalid, name, null, null, error);
    }

    /// <summary>
    /// Parses first. On failure it returns Invalid and writes nothing. On success it writes the §6.2 wrapper atomically,
    /// marks the story dirty once, and returns Ok. I/O exceptions propagate to the caller.
    /// </summary>
    public Task<StoryFingerprint> SaveLexicalFingerprintAsync(string title, byte[] fileBytes, string fileName)
    {
        var parsed = LexicalFingerprintParser.Parse(fileBytes, fileName);
        if (!parsed.Ok) return Task.FromResult(new StoryFingerprint(FingerprintStatus.Invalid, parsed.SuggestedName, null, null, parsed.Error));

        var name = CleanFingerprintTitle(parsed.SuggestedName);
        var uploaded = DateTimeOffset.UtcNow;
        var wrapper = new JsonObject
        {
            ["title"] = name,
            ["uploadedUtc"] = uploaded.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),   // "…Z", as in §6.2
            ["fingerprint"] = parsed.Raw!.DeepClone(),
        };
        var json = wrapper.ToJsonString(FingerprintJsonOptions);

        // No await from here to the move: in WASM the debounced zip save can't see a half-written file (§6.2).
        var path = FingerprintPath(title);
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        StoryFormat.WriteAllText(tmp, json);
        if (File.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        File.Move(tmp, path, overwrite: true);

        _persistence.MarkDirty(title);
        return Task.FromResult(new StoryFingerprint(FingerprintStatus.Ok, name, parsed.Fingerprint, uploaded, null));
    }

    /// <summary>Deletes the file and marks the story dirty. False, with no MarkDirty, when there was no file.</summary>
    public async Task<bool> RemoveLexicalFingerprintAsync(string title)
    {
        var path = FingerprintPath(title);
        if (!File.Exists(path)) return false;

        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        await RetryDeleteAsync(() => File.Delete(path));

        _persistence.MarkDirty(title);
        return true;
    }

    /// <summary>The style card for the AI (§6.4), or null when the story has no usable fingerprint.</summary>
    public async Task<string?> GetFingerprintPromptAsync(string title)
    {
        var fingerprint = await GetLexicalFingerprintAsync(title);
        return fingerprint is { Status: FingerprintStatus.Ok, Fingerprint: { } fp }
            ? FingerprintPromptRenderer.Render(fingerprint.Name ?? LexicalFingerprintDefaults.DefaultName, fp)
            : null;
    }

    /// <summary>LF16: the title is shown in the UI, so control characters and line breaks are removed.</summary>
    static string CleanFingerprintTitle(string? title)
    {
        var clean = new string((title ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) clean = LexicalFingerprintDefaults.DefaultName;
        return clean.Length > LexicalFingerprintDefaults.MaxNameChars ? clean[..LexicalFingerprintDefaults.MaxNameChars] : clean;
    }

    static DateTimeOffset? ReadUploadedUtc(byte[] bytes)
    {
        try
        {
            var span = bytes.AsSpan();
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) span = span[3..];
            var root = JsonNode.Parse(span) as JsonObject;
            if (root?["uploadedUtc"] is JsonValue v && v.GetValueKind() == JsonValueKind.String &&
                DateTimeOffset.TryParse(v.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                return when;
        }
        catch (Exception) { }
        return null;
    }
}
