using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ReverseMarkdown;

namespace WikiConverter;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? source = null;
        string? target = null;
        bool validate = false;
        bool verbose = false;
        bool overwriteHome = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source": source = args[++i]; break;
                case "--target": target = args[++i]; break;
                case "--validate": validate = true; break;
                case "--verbose": verbose = true; break;
                case "--overwrite-home": overwriteHome = true; break;
            }
        }

        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
        {
            Console.Error.WriteLine("Usage: WikiConverter --source <dir> --target <dir> [--validate] [--verbose] [--overwrite-home]");
            return 2;
        }

        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"Source directory does not exist: {source}");
            return 2;
        }

        Directory.CreateDirectory(target);

        var converter = new Converter(source, target, verbose, overwriteHome);
        try
        {
            converter.Run();
            if (validate)
            {
                int errors = converter.Validate();
                if (errors > 0)
                {
                    Console.Error.WriteLine($"Validation failed with {errors} broken link(s).");
                    return 1;
                }
            }
            Console.WriteLine("Conversion complete.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}

internal sealed record PageInfo(string Title, string MarkdownFile);

internal sealed class Converter
{
    private readonly string _source;
    private readonly string _target;
    private readonly bool _verbose;
    private readonly bool _overwriteHome;
    private readonly HashSet<string> _referencedAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PageInfo> _pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default.html"]           = new("Home", "Home.md"),
        ["Installing.html"]        = new("Installing", "Installing.md"),
        ["QuickStart.html"]        = new("Quick Start", "Quick-Start.md"),
        ["Chapters.html"]          = new("Chapters", "Chapters.md"),
        ["Sections.html"]          = new("Sections", "Sections.md"),
        ["Characters.html"]        = new("Characters", "Characters.md"),
        ["Locations.html"]         = new("Locations", "Locations.md"),
        ["Timelines.html"]         = new("Timelines", "Timelines.md"),
        ["WritingStyles.html"]     = new("Writing Styles", "Writing-Styles.md"),
        ["StoryDatabase.html"]     = new("Story Database", "Story-Database.md"),
        ["Settings.html"]          = new("Settings", "Settings.md"),
        ["Logs.html"]              = new("Logs", "Logs.md"),
        ["AnatomyOfAPrompt.html"]  = new("Anatomy Of A Prompt", "Anatomy-Of-A-Prompt.md"),
        ["UtilityFineTuning.html"] = new("Utility Fine Tuning", "Utility-Fine-Tuning.md"),
        ["Details.html"]           = new("Details", "Details.md"),
    };

    public Converter(string source, string target, bool verbose, bool overwriteHome)
    {
        _source = source;
        _target = target;
        _verbose = verbose;
        _overwriteHome = overwriteHome;
    }

    public void Run()
    {
        var converter = BuildMarkdownConverter();

        foreach (var (htmlFile, info) in _pages)
        {
            string srcPath = Path.Combine(_source, htmlFile);
            if (!File.Exists(srcPath))
            {
                Console.WriteLine($"WARN: source page missing: {htmlFile}");
                continue;
            }

            // Skip Home.md unless explicitly overwriting (preserve manual landing page).
            if (string.Equals(info.MarkdownFile, "Home.md", StringComparison.OrdinalIgnoreCase) && !_overwriteHome)
            {
                if (_verbose) Console.WriteLine($"SKIP: {info.MarkdownFile} (already exists; pass --overwrite-home to replace)");
                continue;
            }

            if (_verbose) Console.WriteLine($"Converting {htmlFile} -> {info.MarkdownFile}");

            string html = File.ReadAllText(srcPath, Encoding.UTF8);
            string markdown = ConvertPage(html, info, converter);

            string outPath = Path.Combine(_target, info.MarkdownFile);
            File.WriteAllText(outPath, markdown, new UTF8Encoding(false));
        }

        // Sidebar from default.html nav column.
        string defaultPath = Path.Combine(_source, "default.html");
        if (File.Exists(defaultPath))
        {
            string sidebar = BuildSidebar(File.ReadAllText(defaultPath, Encoding.UTF8));
            File.WriteAllText(Path.Combine(_target, "_Sidebar.md"), sidebar, new UTF8Encoding(false));

            string footer = "[Website](https://aistorybuilders.com/) | " +
                            "[Online version](https://online.aistorybuilders.com/) | " +
                            "[Repository](https://github.com/AIStoryBuilders/AIStoryBuilders)\n";
            File.WriteAllText(Path.Combine(_target, "_Footer.md"), footer, new UTF8Encoding(false));
        }

        CopyAssets();
    }

    private static ReverseMarkdown.Converter BuildMarkdownConverter()
    {
        var config = new Config
        {
            UnknownTags = Config.UnknownTagsOption.Bypass,
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true,
        };
        return new ReverseMarkdown.Converter(config);
    }

    private string ConvertPage(string html, PageInfo info, ReverseMarkdown.Converter md)
    {
        var doc = new HtmlDocument
        {
            OptionFixNestedTags = true,
            OptionAutoCloseOnEnd = true,
        };
        doc.LoadHtml(html);

        // Strip global noise.
        RemoveAll(doc, "//script");
        RemoveAll(doc, "//style");
        RemoveAll(doc, "//meta");
        RemoveAll(doc, "//link[@rel='icon']");
        RemoveAll(doc, "//svg");
        // Strip MS Office <o:p> noise (namespaced; match by local-name).
        RemoveAll(doc, "//*[local-name()='p' and namespace-uri()!='']");
        // Ghost anchors injected by legacy site for heading bookmarks. Their unclosed tags
        // cause ReverseMarkdown to swallow the rest of the page into a giant link.
        UnwrapAll(doc, "//a[@aria-hidden='true']");
        UnwrapAll(doc, "//a[@id and not(@href)]");
        UnwrapAll(doc, "//a[@id and @href and starts-with(@href,'#')]");

        // Collapse internal whitespace in heading text so a multi-line <h3> like
        // "<h3>Online\nWeb Browser Version</h3>" becomes a single-line heading.
        for (int level = 1; level <= 6; level++)
        {
            var headings = doc.DocumentNode.SelectNodes($"//h{level}");
            if (headings == null) continue;
            foreach (var h in headings)
            {
                foreach (var text in h.SelectNodes(".//text()") ?? Enumerable.Empty<HtmlNode>())
                {
                    text.InnerHtml = Regex.Replace(text.InnerHtml, @"\s+", " ");
                }
            }
        }

        HtmlNode body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

        // For default.html, isolate the right-hand pageContent div.
        var pageContent = body.SelectSingleNode(".//div[@id='pageContent']");
        HtmlNode root = pageContent ?? body;

        // Rewrite anchors and image src on every node we keep.
        RewriteLinks(root);
        CollectImageReferences(root);

        // Convert.
        string rawMarkdown = md.Convert(root.InnerHtml);
        string cleaned = PostProcess(rawMarkdown, info);
        return cleaned;
    }

    private void RewriteLinks(HtmlNode root)
    {
        var anchors = root.SelectNodes(".//a");
        if (anchors == null) return;
        var toUnwrap = new List<HtmlNode>();
        foreach (var a in anchors)
            {
                // populateDiv('Page.html') in onclick takes precedence over the # href.
                string onclick = a.GetAttributeValue("onclick", "");
                if (!string.IsNullOrEmpty(onclick))
                {
                    var m = Regex.Match(onclick, @"populateDiv\(\s*['""]([^'""]+\.html)['""]");
                    if (m.Success)
                    {
                        string targetHtml = m.Groups[1].Value;
                        if (_pages.TryGetValue(targetHtml, out var info))
                        {
                            a.SetAttributeValue("href", WikiSlug(info.MarkdownFile));
                            a.Attributes.Remove("onclick");
                            a.Attributes.Remove("target");
                            continue;
                        }
                    }
                }

                string href = a.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(href)) continue;
                if (href.StartsWith("#")) { a.Attributes.Remove("href"); continue; }

                // The legacy documentation site is being decommissioned. Rewrite any
                // links to it so they target the equivalent wiki page (or the wiki Home).
                var docMatch = Regex.Match(
                    href,
                    @"^https?://documentation\.aistorybuilders\.com/?(?<rest>[^?#]*)(?<frag>[#?].*)?$",
                    RegexOptions.IgnoreCase);
                if (docMatch.Success)
                {
                    string rest = docMatch.Groups["rest"].Value;
                    string frag = docMatch.Groups["frag"].Value;
                    if (string.IsNullOrEmpty(rest))
                    {
                        a.SetAttributeValue("href", "Home" + frag);
                    }
                    else if (rest.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                             _pages.TryGetValue(Path.GetFileName(rest), out var docInfo))
                    {
                        a.SetAttributeValue("href", WikiSlug(docInfo.MarkdownFile) + frag);
                    }
                    else
                    {
                        a.SetAttributeValue("href", "Home");
                    }
                    a.Attributes.Remove("target");
                    continue;
                }

                if (href.StartsWith("http://") || href.StartsWith("https://") || href.StartsWith("mailto:")) continue;

                // Strip query/fragment for lookup.
                string file = href;
                string? fragment = null;
                int hashIdx = file.IndexOf('#');
                if (hashIdx >= 0)
                {
                    fragment = file[hashIdx..];
                    file = file[..hashIdx];
                }

                if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(file);
                    if (_pages.TryGetValue(fileName, out var info))
                    {
                        a.SetAttributeValue("href", WikiSlug(info.MarkdownFile) + (fragment ?? ""));
                    }
                    else
                    {
                        // Unknown internal HTML page — unwrap so the link text survives as plain text.
                        toUnwrap.Add(a);
                        continue;
                    }
                }
                else if (file.StartsWith("images/") || file.StartsWith("files/"))
                {
                    _referencedAssets.Add(file.Replace('\\', '/'));
                }
                else if (string.IsNullOrEmpty(file))
                {
                    toUnwrap.Add(a);
                    continue;
                }
                a.Attributes.Remove("target");
            }
        foreach (var a in toUnwrap)
        {
            var parent = a.ParentNode;
            if (parent == null) continue;
            foreach (var child in a.ChildNodes.ToList())
            {
                parent.InsertBefore(child, a);
            }
            parent.RemoveChild(a);
        }
    }

    private static string WikiSlug(string mdFile)
    {
        return Path.GetFileNameWithoutExtension(mdFile);
    }

    private void CollectImageReferences(HtmlNode root)
    {
        var imgs = root.SelectNodes(".//img");
        if (imgs == null) return;
        foreach (var img in imgs)
        {
            string src = img.GetAttributeValue("src", "");
            if (string.IsNullOrEmpty(src)) continue;
            if (src.StartsWith("http://") || src.StartsWith("https://")) continue;
            src = src.Replace('\\', '/');
            // Lowercase extension only.
            string ext = Path.GetExtension(src);
            string normalized = src[..^ext.Length] + ext.ToLowerInvariant();
            if (!string.Equals(normalized, src, StringComparison.Ordinal))
            {
                img.SetAttributeValue("src", normalized);
            }
            _referencedAssets.Add(normalized);
            img.Attributes.Remove("class");
            img.Attributes.Remove("style");
        }
    }

    private static void RemoveAll(HtmlDocument doc, string xpath)
    {
        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes == null) return;
        foreach (var n in nodes.ToList()) n.Remove();
    }

    private static void UnwrapAll(HtmlDocument doc, string xpath)
    {
        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes == null) return;
        foreach (var n in nodes.ToList())
        {
            var parent = n.ParentNode;
            if (parent == null) continue;
            foreach (var child in n.ChildNodes.ToList())
            {
                parent.InsertBefore(child, n);
            }
            parent.RemoveChild(n);
        }
    }

    private static string PostProcess(string md, PageInfo info)
    {
        var lines = md.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();

        var collapsed = new List<string>(lines.Count);
        int blank = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blank++;
                if (blank <= 2) collapsed.Add(line);
            }
            else
            {
                blank = 0;
                collapsed.Add(line);
            }
        }
        string body = string.Join("\n", collapsed).Trim();

        // Merge headings whose text was on the next line in the source HTML
        // (e.g. "###\nRequirements" -> "### Requirements").
        body = Regex.Replace(body, @"(?m)^(#{1,6})\s*\n+(\S)", "$1 $2");

        // Un-escape image-in-link syntax: ReverseMarkdown emits
        // [!\[alt\](src "title")](href). Restore the proper [![alt](src)](href).
        body = Regex.Replace(
            body,
            @"\[!\\\[(?<alt>[^\]]*)\\\]\((?<src>[^)]+)\)\]\((?<href>[^)]+)\)",
            m => $"[![{m.Groups["alt"].Value}]({m.Groups["src"].Value})]({m.Groups["href"].Value})");

        // Strip the legacy "[[home](...)]" back-link rows present in every source page.
        body = Regex.Replace(body, @"(?im)^\s*\[\[home\]\([^)]*\)\]\s*$\r?\n?", string.Empty);

        // GitHub renders the page title automatically from the filename, so strip
        // a leading "# Title" plus the optional "* * *" horizontal-rule separator
        // that the legacy HTML pages emit at the top of every page.
        body = Regex.Replace(body, @"^\s*#\s+[^\r\n]+\r?\n(\s*\*\s\*\s\*\s*\r?\n)?\s*", string.Empty);

        body = body.TrimStart() + "\n";
        return body;
    }

    private string BuildSidebar(string defaultHtml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(defaultHtml);
        var navTd = doc.DocumentNode.SelectSingleNode("//td[contains(@class,'auto-style1')]");
        var sb = new StringBuilder();
        sb.AppendLine("**AIStoryBuilders**");
        sb.AppendLine();
        if (navTd != null)
        {
            foreach (var child in navTd.ChildNodes)
            {
                if (child.NodeType != HtmlNodeType.Element) continue;
                if (child.Name == "p")
                {
                    var strong = child.SelectSingleNode(".//strong");
                    if (strong != null)
                    {
                        string header = HtmlEntity.DeEntitize(strong.InnerText).Trim();
                        if (header.Length > 0 && !header.StartsWith("(AI") &&
                            !header.Contains("Home Website", StringComparison.OrdinalIgnoreCase))
                        {
                            sb.AppendLine();
                            sb.AppendLine($"**{header}**");
                            sb.AppendLine();
                        }
                    }
                }
                else if (child.Name == "ul")
                {
                    foreach (var li in child.SelectNodes(".//li") ?? Enumerable.Empty<HtmlNode>())
                    {
                        var a = li.SelectSingleNode(".//a[contains(@onclick,'populateDiv')]");
                        if (a == null) continue;
                        var m = Regex.Match(a.GetAttributeValue("onclick", ""), @"populateDiv\(\s*['""]([^'""]+\.html)['""]");
                        if (!m.Success) continue;
                        if (!_pages.TryGetValue(m.Groups[1].Value, out var info)) continue;
                        string label = HtmlEntity.DeEntitize(a.InnerText).Trim();
                        label = Regex.Replace(label, @"\s+", " ");
                        sb.AppendLine($"- [{label}]({WikiSlug(info.MarkdownFile)})");
                    }
                }
            }
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        return sb.ToString();
    }

    private void CopyAssets()
    {
        int copied = 0, skipped = 0, missing = 0;
        foreach (var rel in _referencedAssets)
        {
            string srcPath = Path.Combine(_source, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(srcPath))
            {
                string dir = Path.GetDirectoryName(srcPath) ?? _source;
                string name = Path.GetFileName(srcPath);
                if (Directory.Exists(dir))
                {
                    var match = Directory.EnumerateFiles(dir).FirstOrDefault(f =>
                        string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
                    if (match != null) srcPath = match;
                }
            }
            if (!File.Exists(srcPath))
            {
                Console.WriteLine($"WARN: referenced asset not found: {rel}");
                missing++;
                continue;
            }
            string dstPath = Path.Combine(_target, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
            if (File.Exists(dstPath) && SameContent(srcPath, dstPath))
            {
                skipped++;
                continue;
            }
            File.Copy(srcPath, dstPath, overwrite: true);
            copied++;
        }
        Console.WriteLine($"Assets: {copied} copied, {skipped} unchanged, {missing} missing, {_referencedAssets.Count} total referenced.");
    }

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        using var ha = System.Security.Cryptography.SHA256.Create();
        using var hb = System.Security.Cryptography.SHA256.Create();
        var hashA = Convert.ToHexString(ha.ComputeHash(sa));
        var hashB = Convert.ToHexString(hb.ComputeHash(sb));
        return hashA == hashB;
    }

    public int Validate()
    {
        int errors = 0;
        var mdFiles = Directory.EnumerateFiles(_target, "*.md", SearchOption.TopDirectoryOnly).ToList();
        var slugSet = new HashSet<string>(mdFiles.Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);

        var linkRx = new Regex(@"\[(?<text>[^\]]*)\]\((?<href>[^)]+)\)", RegexOptions.Compiled);
        var imgRx = new Regex(@"!\[(?<alt>[^\]]*)\]\((?<src>[^)]+)\)", RegexOptions.Compiled);

        foreach (var file in mdFiles)
        {
            string text = File.ReadAllText(file);

            foreach (Match m in imgRx.Matches(text))
            {
                string src = m.Groups["src"].Value.Split(' ')[0].Trim();
                if (src.StartsWith("http://") || src.StartsWith("https://")) continue;
                string fullPath = Path.Combine(_target, src.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    Console.Error.WriteLine($"BROKEN IMG in {Path.GetFileName(file)}: {src}");
                    errors++;
                }
            }

            foreach (Match m in linkRx.Matches(text))
            {
                int idx = m.Index;
                if (idx > 0 && text[idx - 1] == '!') continue;

                string href = m.Groups["href"].Value.Split(' ')[0].Trim();
                if (href.StartsWith("http://") || href.StartsWith("https://") || href.StartsWith("mailto:")) continue;
                if (href.StartsWith("#")) continue;

                int hash = href.IndexOf('#');
                if (hash >= 0) href = href[..hash];
                if (string.IsNullOrEmpty(href)) continue;

                if (href.Contains('/'))
                {
                    string fullPath = Path.Combine(_target, href.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullPath))
                    {
                        Console.Error.WriteLine($"BROKEN ASSET LINK in {Path.GetFileName(file)}: {href}");
                        errors++;
                    }
                }
                else
                {
                    if (!slugSet.Contains(href))
                    {
                        Console.Error.WriteLine($"BROKEN WIKI LINK in {Path.GetFileName(file)}: {href}");
                        errors++;
                    }
                }
            }
        }
        Console.WriteLine($"Validation: {errors} broken link(s) across {mdFiles.Count} page(s).");
        return errors;
    }
}
