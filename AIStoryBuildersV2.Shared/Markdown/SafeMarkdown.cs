using System.Net;
using AIStoryBuildersV2.Shared.Knowledge;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AIStoryBuildersV2.Shared.Markdown;

/// <summary>
/// Renders Markdown to HTML for the preview and for chat. Raw HTML is never rendered, images become text,
/// and in chat external links are inert (D17). Page links go through the story's <see cref="KbLinkBuilder"/> (D13).
/// </summary>
public static class SafeMarkdown
{
    public static string ForPreview(string markdown, KbLinkBuilder links, Func<string, string?> resolve, Func<string, string?>? resolveRelative = null, string? currentPath = null) =>
        Render(markdown, new Options(links, resolve, resolveRelative, Chat: false, currentPath));

    /// <summary>Chat answers: <paramref name="links"/> null renders <c>[[path]]</c> citations as plain text.</summary>
    public static string ForChat(string markdown, KbLinkBuilder? links, Func<string, string?> resolve) =>
        Render(markdown, new Options(links, resolve, null, Chat: true, null));

    sealed record Options(KbLinkBuilder? Links, Func<string, string?> Resolve, Func<string, string?>? ResolveRelative, bool Chat, string? CurrentPath);

    static string Render(string markdown, Options options)
    {
        var (_, body) = FrontMatter.Split(markdown ?? "");
        var doc = Markdig.Markdown.Parse(body, MarkdownPipelines.Render);
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        MarkdownPipelines.Render.Setup(renderer);

        renderer.ObjectRenderers.RemoveAll(r => r is LinkInlineRenderer or AutolinkInlineRenderer);
        renderer.ObjectRenderers.Insert(0, new WikiRenderer(options));
        renderer.ObjectRenderers.Insert(0, new SafeLinkRenderer(options));
        renderer.ObjectRenderers.Insert(0, new SafeAutolinkRenderer(options));

        renderer.Render(doc);
        writer.Flush();
        return writer.ToString();
    }

    static string E(string s) => WebUtility.HtmlEncode(s);

    static readonly HashSet<string> ClickableSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    /// <summary>Only http, https and mailto become anchors (SEC-002). Uri parsing also catches "javascript://…".</summary>
    internal static bool IsSafeExternalUrl(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && ClickableSchemes.Contains(uri.Scheme);

    static void WriteExternal(HtmlRenderer r, string text, string url, Options o)
    {
        if (o.Chat || !IsSafeExternalUrl(url))
        {
            // Inert: no href, URL visible.
            r.Write("<span class=\"ext\" title=\"External link (not clickable)\">")
             .Write(E(text));
            if (!string.Equals(text, url, StringComparison.Ordinal)) r.Write(" ");
            if (!string.Equals(text, url, StringComparison.Ordinal)) r.Write("<span class=\"ext-url\">").Write(E(url)).Write("</span>");
            r.Write("</span>");
        }
        else
        {
            r.Write("<a class=\"ext-link\" href=\"").Write(E(url)).Write("\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">")
             .Write(E(text)).Write("</a>");
        }
    }

    sealed class WikiRenderer(Options o) : HtmlObjectRenderer<WikiLinkInline>
    {
        protected override void Write(HtmlRenderer r, WikiLinkInline link)
        {
            var text = link.DisplayText;
            if (o.Links is null)
            {
                r.Write(E(text));
                return;
            }
            var resolved = o.Resolve(link.Target);
            if (resolved is not null)
            {
                var cls = o.Chat ? "cite" : "wl";
                r.Write("<a class=\"").Write(cls).Write("\" href=\"").Write(E(o.Links.PageHref(resolved))).Write("\" title=\"")
                 .Write(E(KnowledgePath.StripExtension(resolved))).Write("\">");
                if (o.Chat) r.Write("<span class=\"mi\" aria-hidden=\"true\">description</span>");
                r.Write(E(text)).Write("</a>");
            }
            else
            {
                r.Write("<a class=\"wu\" href=\"").Write(E(o.Links.CreateHref(link.Target))).Write("\" title=\"Unresolved: create ")
                 .Write(E(link.Target)).Write("\"><span class=\"mi\" aria-hidden=\"true\">link_off</span>")
                 .Write(E(text)).Write("<span class=\"sr-only\"> (unresolved)</span></a>");
            }
        }
    }

    sealed class SafeLinkRenderer(Options o) : HtmlObjectRenderer<LinkInline>
    {
        protected override void Write(HtmlRenderer r, LinkInline link)
        {
            var label = PlainLabel(link);
            var url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? "";

            if (link.IsImage)
            {
                r.Write("<span class=\"img-placeholder\" title=\"Images are not shown\"><span class=\"mi\" aria-hidden=\"true\">image</span>")
                 .Write(E(label.Length > 0 ? label : "image")).Write("</span>");
                return;
            }

            if (url.StartsWith('#'))
            {
                r.Write("<span class=\"anchor-link\">").Write(E(label)).Write("</span>");
                return;
            }

            if (MarkdownPageParser.IsExternal(url))
            {
                WriteExternal(r, label.Length > 0 ? label : url, url, o);
                return;
            }

            // KGB-style page links (./?p=Page.md) keep working, pointed at this story's KB.
            if (url.StartsWith("./?p=", StringComparison.Ordinal) || url.StartsWith("?p=", StringComparison.Ordinal))
            {
                var page = Uri.UnescapeDataString(url[(url.IndexOf("?p=", StringComparison.Ordinal) + 3)..].Split('&')[0]);
                if (o.Links is null) r.Write(E(label));
                else r.Write("<a class=\"wl\" href=\"").Write(E(o.Links.PageHref(page))).Write("\">").Write(E(label)).Write("</a>");
                return;
            }

            var resolved = o.ResolveRelative?.Invoke(url);
            if (resolved is not null && o.Links is not null)
                r.Write("<a class=\"wl\" href=\"").Write(E(o.Links.PageHref(resolved))).Write("\">").Write(E(label)).Write("</a>");
            else
                r.Write("<span class=\"wu-text\" title=\"Unresolved link: ").Write(E(url)).Write("\">").Write(E(label)).Write("</span>");
        }

        static string PlainLabel(ContainerInline container)
        {
            var parts = new List<string>();
            foreach (var child in container.Descendants<Inline>())
            {
                if (child is LiteralInline lit) parts.Add(lit.Content.ToString());
                else if (child is CodeInline code) parts.Add(code.Content);
                else if (child is WikiLinkInline w) parts.Add(w.DisplayText);
            }
            return string.Concat(parts);
        }
    }

    sealed class SafeAutolinkRenderer(Options o) : HtmlObjectRenderer<AutolinkInline>
    {
        protected override void Write(HtmlRenderer r, AutolinkInline link)
        {
            var url = link.IsEmail ? "mailto:" + link.Url : link.Url;
            WriteExternal(r, link.Url, url, o);
        }
    }
}
