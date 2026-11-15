using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AIStoryBuildersV2.Shared.Markdown;

/// <summary>A rendered help page: its HTML and the level-2 sections for the contents list.</summary>
public sealed record HelpDocument(string Html, IReadOnlyList<HelpSection> Sections);

public sealed record HelpSection(string Id, string Title);

/// <summary>
/// Renders Docs/User-Guide.md (embedded at build time) for the Help page, so the page and the guide never drift apart.
/// Heading ids match GitHub's, so <c>help#1-connect-an-ai-provider</c> is the same anchor as on GitHub.
/// Raw HTML stays disabled even though the guide is ours.
/// </summary>
public static partial class HelpMarkdown
{
    /// <summary>Where the guide's screenshots are served from. They stay in Docs/images/user-guide/ (see the .csproj).</summary>
    public const string ImageBase = "_content/AIStoryBuildersV2.Shared/images/user-guide/";

    const string GuideResource = "Help/User-Guide.md";

    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().UseAutoIdentifiers(AutoIdentifierOptions.GitHub).DisableHtml().Build();

    public static HelpDocument LoadGuide(string pagePath = "help")
    {
        using var stream = typeof(HelpMarkdown).Assembly.GetManifestResourceStream(GuideResource)
            ?? throw new InvalidOperationException($"Embedded resource '{GuideResource}' is missing.");
        using var reader = new StreamReader(stream);
        return Render(reader.ReadToEnd(), pagePath);
    }

    public static HelpDocument Render(string markdown, string pagePath = "help")
    {
        var doc = Markdig.Markdown.Parse(markdown ?? "", Pipeline);
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);

        var fallback = renderer.ObjectRenderers.FindExact<CodeBlockRenderer>() ?? new CodeBlockRenderer();
        renderer.ObjectRenderers.RemoveAll(r => r is Markdig.Renderers.Html.Inlines.LinkInlineRenderer or Markdig.Renderers.Html.Inlines.AutolinkInlineRenderer);
        renderer.ObjectRenderers.Insert(0, new LinkRenderer(pagePath));
        renderer.ObjectRenderers.Insert(0, new FlowchartRenderer(fallback));

        renderer.Render(doc);
        writer.Flush();

        var sections = doc.Descendants<HeadingBlock>()
            .Where(h => h.Level == 2 && h.GetAttributes().Id is { Length: > 0 })
            .Select(h => new HelpSection(h.GetAttributes().Id!, PlainText(h.Inline)))
            .ToList();
        return new HelpDocument(writer.ToString(), sections);
    }

    static string E(string s) => WebUtility.HtmlEncode(s);

    static string PlainText(ContainerInline? container) =>
        container is null ? "" : string.Concat(container.Descendants<Inline>().Select(i => i switch
        {
            LiteralInline lit => lit.Content.ToString(),
            CodeInline code => code.Content,
            _ => "",
        }));

    sealed class LinkRenderer(string pagePath) : HtmlObjectRenderer<LinkInline>
    {
        protected override void Write(HtmlRenderer r, LinkInline link)
        {
            var url = link.Url ?? "";
            var label = PlainText(link);

            if (link.IsImage)
            {
                // Only the guide's own screenshots are shown; the file name is all that's kept.
                if (url.StartsWith("images/user-guide/", StringComparison.Ordinal) && !url.Contains("..", StringComparison.Ordinal))
                    r.Write("<img src=\"").Write(E(ImageBase + url["images/user-guide/".Length..])).Write("\" alt=\"").Write(E(label))
                     .Write("\" loading=\"lazy\" />");
                else
                    r.Write(E(label));
                return;
            }

            if (url.StartsWith('#'))
            {
                // With <base href="/">, a bare "#id" would point at the library page.
                r.Write("<a href=\"").Write(E(pagePath + url)).Write("\">");
                r.WriteChildren(link);
                r.Write("</a>");
                return;
            }

            if (SafeMarkdown.IsSafeExternalUrl(url))
            {
                r.Write("<a href=\"").Write(E(url)).Write("\" target=\"_blank\" rel=\"noopener noreferrer\">");
                r.WriteChildren(link);
                r.Write("</a>");
                return;
            }

            r.WriteChildren(link);
        }
    }

    /// <summary>
    /// The app ships no Mermaid script (D18), so a <c>mermaid</c> flowchart is shown as its list of steps:
    /// one row per arrow, with the node labels. Other code blocks render as usual.
    /// </summary>
    sealed class FlowchartRenderer(IMarkdownObjectRenderer fallback) : HtmlObjectRenderer<CodeBlock>
    {
        protected override void Write(HtmlRenderer r, CodeBlock block)
        {
            if (block is not FencedCodeBlock { Info: "mermaid" } fenced)
            {
                fallback.Write(r, block);
                return;
            }

            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            var edges = new List<(string From, string To)>();
            foreach (var line in fenced.Lines.Lines.Take(fenced.Lines.Count))
            {
                var m = EdgePattern().Match(line.Slice.ToString());
                if (!m.Success) continue;
                if (m.Groups[2].Success) labels[m.Groups[1].Value] = m.Groups[2].Value;
                if (m.Groups[4].Success) labels[m.Groups[3].Value] = m.Groups[4].Value;
                edges.Add((m.Groups[1].Value, m.Groups[3].Value));
            }

            r.Write("<ol class=\"asb-help-flow\">");
            foreach (var (from, to) in edges)
            {
                r.Write("<li><span class=\"asb-help-node\">").Write(E(labels.GetValueOrDefault(from, from)))
                 .Write("</span><span class=\"mi\" aria-hidden=\"true\">arrow_forward</span><span class=\"sr-only\"> leads to </span><span class=\"asb-help-node\">")
                 .Write(E(labels.GetValueOrDefault(to, to))).Write("</span></li>");
            }
            r.Write("</ol>");
        }
    }

    /// <summary><c>A[Label] --> B[Label]</c>, where either label is optional.</summary>
    [GeneratedRegex(@"^\s*(\w+)(?:\[([^\]]*)\])?\s*-->\s*(\w+)(?:\[([^\]]*)\])?\s*$")]
    private static partial Regex EdgePattern();
}
