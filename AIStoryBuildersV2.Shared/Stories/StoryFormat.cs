using System.Globalization;
using System.Text;

namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>
/// Reading and writing V1's story file conventions (Docs/V1-File-Format.md). V2 writes only records V1 can read (D32)
/// and reads V1's quirks one record at a time. Vectors are invariant; timeline dates use V1's current-culture short form (D30).
/// </summary>
public static class StoryFormat
{
    /// <summary>V1 runs on Windows, so its files use CRLF even when V2 writes them from the browser.</summary>
    public const string NewLine = "\r\n";

    /// <summary>all-MiniLM-L6-v2 output size. A vector with any other count is treated as missing (D30).</summary>
    public const int VectorDimensions = 384;

    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Windows' invalid file-name characters, fixed so the browser (where Path.GetInvalidFileNameChars is only
    // '\0' and '/') names files the way V1 does on Windows.
    static readonly char[] InvalidFileNameChars =
        ['"', '<', '>', '|', ':', '*', '?', '\\', '/', .. Enumerable.Range(0, 32).Select(i => (char)i)];

    static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>V1's SanitizeFileName: removes invalid file-name characters and '|'. It does not trim.</summary>
    public static string SanitizeFileName(string? input) =>
        new((input ?? "").Where(ch => !InvalidFileNameChars.Contains(ch)).ToArray());

    /// <summary>D32 for every field except paragraph content: delete '|' and turn line breaks into spaces.</summary>
    public static string CleanField(string? value) =>
        (value ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace("|", "");

    /// <summary>D32 for paragraph content: delete '|' and write line breaks as CRLF, as V1's UpdateParagraph does.</summary>
    public static string CleanContent(string? value) =>
        NormalizeLineBreaks((value ?? "").Replace("|", "")).Replace("\n", NewLine);

    /// <summary>Converts CRLF, V1's CR-CR-LF and lone CRs to LF.</summary>
    public static string NormalizeLineBreaks(string text) => text.Replace("\r\n", "\n").Replace("\r", "");

    /// <summary>Splits on LF the way V1's CSV and log readers do, and strips the CRs they leave behind.</summary>
    public static string[] SplitLinesLf(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return [.. lines];
    }

    /// <summary>V1's File.WriteAllLines on Windows: UTF-8 without BOM, CRLF after every line, the last one included.</summary>
    public static void WriteAllLines(string path, IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines) sb.Append(line).Append(NewLine);
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    /// <summary>V1's File.WriteAllText: UTF-8 without BOM, nothing added.</summary>
    public static void WriteAllText(string path, string text) => File.WriteAllText(path, text, Utf8NoBom);

    public static Task WriteAllTextAsync(string path, string text) => File.WriteAllTextAsync(path, text, Utf8NoBom);

    /// <summary>Non-blank lines, as V1's per-story readers see them.</summary>
    public static string[] ReadNonBlankLines(string path) =>
        File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToArray();

    // ---------- vectors ----------

    /// <summary>"[v1,v2,...]" with invariant-culture floats (D30). V1 used the current culture.</summary>
    public static string FormatVector(ReadOnlySpan<float> vector)
    {
        var sb = new StringBuilder(vector.Length * 12).Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString(CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }

    /// <summary>Parses a vector field. Returns null unless it has exactly 384 numbers (a comma-decimal V1 vector fails).</summary>
    public static float[]? ParseVector(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return null;
        var text = field.Trim();
        if (!text.StartsWith('[') || !text.EndsWith(']')) return null;
        var parts = text[1..^1].Split(',');
        if (parts.Length != VectorDimensions) return null;
        var vector = new float[VectorDimensions];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vector[i])) return null;
        }
        return vector;
    }

    /// <summary>V1's guard for a vector that ended up in a text field.</summary>
    public static bool LooksLikeVector(string? text) =>
        !string.IsNullOrEmpty(text) && text.TrimStart().StartsWith('[') && text.Contains(',');

    // ---------- dates ----------

    /// <summary>V1's timeline date: current-culture short date + " " + short time (D30).</summary>
    public static string FormatTimelineDate(DateTime value) => value.ToShortDateString() + " " + value.ToShortTimeString();

    /// <summary>Reads a date with the current culture, then en-US, then invariant; U+202F and U+00A0 count as spaces (D30).</summary>
    public static bool TryParseDate(string? text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = text.Replace(' ', ' ').Replace(' ', ' ').Trim();
        var cultures = new[] { CultureInfo.CurrentCulture, EnUs, CultureInfo.InvariantCulture };

        // Exact patterns first: a loose parse would read en-US "11/1/2026" day-first under de-DE.
        foreach (var culture in cultures)
        {
            var patterns = culture.DateTimeFormat.GetAllDateTimePatterns('g')
                .Concat(culture.DateTimeFormat.GetAllDateTimePatterns('G'))
                .Select(p => p.Replace(' ', ' ').Replace(' ', ' '))
                .ToArray();
            if (DateTime.TryParseExact(normalized, patterns, culture, DateTimeStyles.AllowWhiteSpaces, out value)) return true;
        }
        foreach (var culture in cultures)
        {
            if (DateTime.TryParse(normalized, culture, DateTimeStyles.AllowWhiteSpaces, out value)) return true;
        }
        return false;
    }

    // ---------- paragraphs ----------

    /// <summary>A paragraph's character list: '[' and ']' removed, split on ',', trimmed, empty entries dropped.</summary>
    public static List<string> ParseCharacterList(string? field) =>
        (field ?? "").Replace("[", "").Replace("]", "")
            .Split(',')
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToList();

    /// <summary>"[a,b]" or "[]", never V1's lone "]" (D32). Names lose '|', '[', ']', ',' and line breaks.</summary>
    public static string FormatCharacterList(IEnumerable<string?>? names) =>
        "[" + string.Join(",", (names ?? [])
            .Select(n => CleanField(n).Replace("[", "").Replace("]", "").Replace(",", "").Trim())
            .Where(n => n.Length > 0)) + "]";

    public sealed record ParagraphRecord(string Location, string Timeline, List<string> Characters, string Content, string Vector);

    /// <summary>
    /// Parses a paragraph file. Content keeps its line breaks (as LF) and blank lines. The last field is the vector
    /// only when it looks like one, and any extra middle fields are joined back into the content (V1-File-Format §7.2).
    /// </summary>
    /// <returns>null when the record has fewer than 4 fields.</returns>
    public static ParagraphRecord? ParseParagraph(string fileText)
    {
        var text = NormalizeLineBreaks(fileText).Trim('\n');
        var parts = text.Split('|');
        if (parts.Length < 4) return null;

        string content, vector = "";
        var last = parts[^1].Trim();
        if (parts.Length >= 5 && (last.Length == 0 || last.StartsWith('[')))
        {
            vector = last;
            content = string.Join("|", parts[3..^1]);
        }
        else
        {
            content = string.Join("|", parts[3..]);
        }
        return new ParagraphRecord(parts[0].Trim('\n'), parts[1], ParseCharacterList(parts[2]), content.Trim('\n'), vector);
    }

    /// <summary>"loc|tl|[a,b]|content|vector" with D32 cleaning. Content line breaks become CRLF.</summary>
    public static string FormatParagraph(string? location, string? timeline, IEnumerable<string?>? characters, string? content, string vector) =>
        $"{CleanField(location)}|{CleanField(timeline)}|{FormatCharacterList(characters)}|{CleanContent(content)}|{vector}";

    // ---------- chapters ----------

    static readonly Regex ChapterFolder = new(@"^Chapter(\d+)$", RegexOptions.CultureInvariant);
    static readonly Regex ParagraphFile = new(@"^Paragraph(\d+)$", RegexOptions.CultureInvariant);

    /// <summary>The N of a "Chapter{N}" folder name, or null for any other name.</summary>
    public static int? ChapterNumber(string folderName) =>
        ChapterFolder.Match(folderName) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n) ? n : null;

    /// <summary>The M of a "Paragraph{M}" file name (without extension), or null.</summary>
    public static int? ParagraphNumber(string fileNameWithoutExtension) =>
        ParagraphFile.Match(fileNameWithoutExtension) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
}
