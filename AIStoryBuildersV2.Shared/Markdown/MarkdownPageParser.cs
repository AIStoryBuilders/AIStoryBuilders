using System.Text.RegularExpressions;
using AIStoryBuildersV2.Shared.Knowledge;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AIStoryBuildersV2.Shared.Markdown;

/// <param name="SpanStart">Start of the whole link in the page text.</param>
/// <param name="UrlStart">For Markdown links, the start of the URL inside the link (-1 for wiki links).</param>
public sealed record ParsedLink(string RawTarget, string? Heading, string? Alias, LinkKind Kind,
                                int SpanStart, int SpanLength, int Line, int UrlStart = -1, int UrlLength = 0);

public sealed record ParsedPage(string Path, string Title, FrontMatterData FrontMatter, string? FrontMatterError,
                                IReadOnlyList<ParsedLink> Links, string PlainText);

public sealed partial class MarkdownPageParser
{
    [GeneratedRegex(@"\[\[([^\]\n]*)\]\]")]
    private static partial Regex WikiText();

    [GeneratedRegex(@"[*_`>#~|\[\]()!-]+")]
    private static partial Regex MarkupChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public ParsedPage Parse(string path, string markdown)
    {
        markdown ??= "";
        var (yaml, body) = FrontMatter.Split(markdown);
        var front = FrontMatter.Parse(yaml, out var error);
        var title = front.Title ?? DefaultTitle(path);

        var doc = Markdig.Markdown.Parse(markdown, MarkdownPipelines.Parse);
        var links = new List<ParsedLink>();
        foreach (var inline in doc.Descendants<Inline>())
        {
            switch (inline)
            {
                case WikiLinkInline w:
                    links.Add(new ParsedLink(w.Target, w.Heading, w.Alias, LinkKind.Wiki, w.Span.Start, w.Span.Length, w.Line + 1));
                    break;
                case LinkInline { IsImage: false, IsAutoLink: false } l when !string.IsNullOrEmpty(l.Url) && !IsExternal(l.Url):
                    links.Add(new ParsedLink(l.Url, null, null, LinkKind.Markdown, l.Span.Start, l.Span.Length, l.Line + 1,
                        l.UrlSpan.IsEmpty ? -1 : l.UrlSpan.Start, l.UrlSpan.IsEmpty ? 0 : l.UrlSpan.Length));
                    break;
            }
        }
        links.Sort((a, b) => a.SpanStart.CompareTo(b.SpanStart));

        return new ParsedPage(path, title, front, error, links, ToPlainText(body));
    }

    public static string DefaultTitle(string path)
    {
        var name = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        return KnowledgePath.StripExtension(name);
    }

    public static bool IsExternal(string url) =>
        url.StartsWith('#') ||
        url.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("://", StringComparison.Ordinal);

    static string ToPlainText(string body)
    {
        var text = WikiText().Replace(body, m =>
        {
            WikiLinkInlineParser.TryParseInner(m.Groups[1].Value, out var target, out _, out var alias);
            return alias ?? target;
        });
        text = MarkupChars().Replace(text, " ");
        return Whitespace().Replace(text, " ").Trim();
    }
}
