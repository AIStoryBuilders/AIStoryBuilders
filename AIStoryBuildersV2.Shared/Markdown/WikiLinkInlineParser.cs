using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AIStoryBuildersV2.Shared.Markdown;

/// <summary><c>[[target#heading|alias]]</c></summary>
public sealed class WikiLinkInline : LeafInline
{
    public required string Target { get; init; }
    public string? Heading { get; init; }
    public string? Alias { get; init; }
    public string DisplayText => Alias ?? (Heading is null ? Target : $"{Target} › {Heading}");
}

/// <summary>
/// Parses wiki links. Markdig only calls inline parsers outside code spans and code blocks, so links in code are skipped.
/// </summary>
public sealed class WikiLinkInlineParser : InlineParser
{
    public WikiLinkInlineParser() => OpeningCharacters = ['['];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.PeekChar() != '[') return false;
        var text = slice.Text;
        var start = slice.Start;
        var innerStart = start + 2;
        var close = -1;
        for (var i = innerStart; i < slice.End; i++)
        {
            var c = text[i];
            if (c is '\n' or '\r' or '[') return false;
            if (c == ']')
            {
                if (text[i + 1] != ']') return false;
                close = i;
                break;
            }
        }
        if (close <= innerStart) return false;

        if (!TryParseInner(text[innerStart..close], out var target, out var heading, out var alias)) return false;

        var startPosition = processor.GetSourcePosition(start, out var line, out var column);
        var endPosition = processor.GetSourcePosition(close + 1);
        processor.Inline = new WikiLinkInline
        {
            Target = target,
            Heading = heading,
            Alias = alias,
            Span = new SourceSpan(startPosition, endPosition),
            Line = line,
            Column = column,
        };
        slice.Start = close + 2;
        return true;
    }

    public static bool TryParseInner(string inner, out string target, out string? heading, out string? alias)
    {
        heading = null;
        alias = null;
        var pipe = inner.IndexOf('|');
        if (pipe >= 0)
        {
            alias = inner[(pipe + 1)..].Trim();
            if (alias.Length == 0) alias = null;
            inner = inner[..pipe];
        }
        var hash = inner.IndexOf('#');
        if (hash >= 0)
        {
            heading = inner[(hash + 1)..].Trim();
            if (heading.Length == 0) heading = null;
            inner = inner[..hash];
        }
        target = inner.Trim();
        if (target.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) target = target[..^3].TrimEnd();
        return target.Length > 0;
    }
}

public sealed class WikiLinkExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<WikiLinkInlineParser>())
            pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new WikiLinkInlineParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }
}

public static class MarkdownPipelines
{
    /// <summary>For parsing links with precise spans.</summary>
    public static readonly MarkdownPipeline Parse = Build(new MarkdownPipelineBuilder().UsePreciseSourceLocation());

    /// <summary>For rendering preview and chat HTML. Raw HTML is disabled.</summary>
    public static readonly MarkdownPipeline Render = Build(new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseEmphasisExtras().UseAutoLinks().DisableHtml());

    static MarkdownPipeline Build(MarkdownPipelineBuilder builder)
    {
        builder.UseYamlFrontMatter();
        builder.Extensions.AddIfNotAlready<WikiLinkExtension>();
        return builder.Build();
    }
}
