using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace AIStoryBuildersV2.Shared.Markdown;

public sealed record FrontMatterData(string? Title, IReadOnlyList<string> Tags, IReadOnlyList<string> Aliases,
                                     IReadOnlyList<string> Sources, DateOnly? Updated)
{
    public static readonly FrontMatterData Empty = new(null, [], [], [], null);
}

/// <summary>Splits YAML front-matter from the body. The YAML text is kept verbatim.</summary>
public static class FrontMatter
{
    static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    public static (string? Yaml, string Body) Split(string markdown)
    {
        markdown ??= "";
        var nl = markdown.StartsWith("---\r\n", StringComparison.Ordinal) ? "\r\n"
               : markdown.StartsWith("---\n", StringComparison.Ordinal) ? "\n"
               : null;
        if (nl is null) return (null, markdown);

        var pos = 3 + nl.Length;
        var yamlStart = pos;
        while (pos <= markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', pos);
            var line = lineEnd < 0 ? markdown[pos..] : markdown[pos..lineEnd];
            var trimmed = line.TrimEnd('\r', ' ', '\t');
            if (trimmed is "---" or "...")
            {
                var yamlEnd = pos;
                var yaml = yamlEnd > yamlStart ? markdown[yamlStart..yamlEnd] : "";
                if (yaml.EndsWith(nl, StringComparison.Ordinal)) yaml = yaml[..^nl.Length];
                else if (yaml.EndsWith('\n')) yaml = yaml[..^1];
                var body = lineEnd < 0 ? "" : markdown[(lineEnd + 1)..];
                return (yaml, body);
            }
            if (lineEnd < 0) break;
            pos = lineEnd + 1;
        }
        return (null, markdown);
    }

    public static string Join(string? yaml, string body)
    {
        if (yaml is null) return body ?? "";
        var nl = (yaml.Contains("\r\n") || (body ?? "").Contains("\r\n")) ? "\r\n" : "\n";
        return yaml.Length == 0
            ? "---" + nl + "---" + nl + body
            : "---" + nl + yaml + nl + "---" + nl + body;
    }

    public static FrontMatterData Parse(string? yaml, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(yaml)) return FrontMatterData.Empty;
        object? root;
        try
        {
            root = Deserializer.Deserialize<object>(yaml);
        }
        catch (YamlException ex)
        {
            error = $"Line {ex.Start.Line}: {FirstLine(ex.Message)}";
            return FrontMatterData.Empty;
        }

        if (root is null) return FrontMatterData.Empty;
        if (root is not IDictionary<object, object> map)
        {
            error = "Front-matter must be a set of key: value pairs.";
            return FrontMatterData.Empty;
        }

        string? Scalar(string key) => Get(map, key) is { } v and not IEnumerable<object> and not IDictionary<object, object> ? v.ToString() : null;

        DateOnly? updated = null;
        if (Scalar("updated") is { } u &&
            DateOnly.TryParse(u.Length >= 10 ? u[..10] : u, CultureInfo.InvariantCulture, out var d))
            updated = d;

        return new FrontMatterData(
            string.IsNullOrWhiteSpace(Scalar("title")) ? null : Scalar("title")!.Trim(),
            List(Get(map, "tags")),
            List(Get(map, "aliases") ?? Get(map, "alias")),
            List(Get(map, "sources")),
            updated);
    }

    static object? Get(IDictionary<object, object> map, string key)
    {
        foreach (var (k, v) in map)
        {
            if (string.Equals(k?.ToString(), key, StringComparison.OrdinalIgnoreCase)) return v;
        }
        return null;
    }

    static IReadOnlyList<string> List(object? value) => value switch
    {
        null => [],
        string s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        IEnumerable<object> items => items.Where(i => i is not null and not IDictionary<object, object>)
                                          .Select(i => i.ToString()!.Trim()).Where(i => i.Length > 0).ToList(),
        _ => [value.ToString()!],
    };

    static string FirstLine(string message)
    {
        var text = Regex.Replace(message, @"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\): ", "");
        var i = text.IndexOf('\n');
        return i < 0 ? text : text[..i];
    }
}

/// <summary>Undoes the escaping that the Markdown editor applies to wiki links.</summary>
public static partial class EditorSanitizer
{
    [GeneratedRegex(@"\\?\[\\?\[(?<inner>(?:\\.|[^\]\n\\])+?)\\?\]\\?\]")]
    private static partial Regex EscapedWikiLink();

    [GeneratedRegex(@"\\([\[\]|#_*\\])")]
    private static partial Regex EscapedChar();

    public static string UnescapeWikiLinks(string body)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains('[')) return body ?? "";
        return EscapedWikiLink().Replace(body, m =>
        {
            if (!m.Value.Contains('\\')) return m.Value;
            return "[[" + EscapedChar().Replace(m.Groups["inner"].Value, "$1") + "]]";
        });
    }
}
