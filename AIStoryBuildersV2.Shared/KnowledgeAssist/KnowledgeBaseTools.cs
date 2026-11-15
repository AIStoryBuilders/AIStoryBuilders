using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Markdown;
using AIStoryBuildersV2.Shared.Models;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.KnowledgeAssist;

/// <summary>Plan §6.4 constants for KB retrieval and tools.</summary>
public static class KnowledgeAssistConstants
{
    public const int MaxIterations = 4;
    public const int MaxKnowledgeNotesChars = 4000;
    public static readonly TimeSpan RetrieveTimeout = TimeSpan.FromSeconds(60);
    public const string NoRelevantPages = "NO_RELEVANT_PAGES";
}

/// <summary>
/// D14: KGB's four read tools for one story's KB, renamed with the <c>kb_</c> prefix so they never clash with the story
/// graph tools, plus the page edits Story Chat proposes (<see cref="PlanAsync"/>, <see cref="ApplyAsync"/>). Paths are
/// parsed with <see cref="KnowledgePath"/>, so they stay inside the KB.
/// </summary>
public sealed class KnowledgeBaseTools(IStoryKnowledgeBase kb, int maxToolResultChars = 20_000)
{
    public const string Prefix = "kb_";

    public IList<AITool> CreateReadOnly() =>
    [
        AIFunctionFactory.Create(SearchPagesAsync, Prefix + "search_pages", "Keyword search over the story's knowledge base: page titles, aliases, tags, paths and text. Returns the best matches with snippets."),
        AIFunctionFactory.Create(ReadPageAsync, Prefix + "read_page", "Reads a knowledge base page's full Markdown, including front-matter."),
        AIFunctionFactory.Create(ListFolderAsync, Prefix + "list_folder", "Lists the pages and folders in a knowledge base folder. Use \"\" for the root."),
        AIFunctionFactory.Create(GetLinksAsync, Prefix + "get_links", "Lists a knowledge base page's outgoing links and backlinks."),
    ];

    IKnowledgeWorkspace Workspace => kb.Workspace;

    static string Json(object value) => JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions);

    static string Error(string message) => Json(new { error = message });

    static bool TryPage(string? path, out KnowledgePath page, [NotNullWhen(false)] out string? error) =>
        KnowledgePath.TryParse(path?.Trim().TrimStart('/'), isPage: true, out page, out error);

    public async Task<string> SearchPagesAsync(
        [Description("Keywords to search for")] string query,
        [Description("Maximum results, 1 to 20")] int top = 8,
        CancellationToken ct = default)
    {
        var hits = await Workspace.Graph.SearchAsync(query ?? "", Math.Clamp(top, 1, 20), ct);
        return Json(hits.Select(h => new { path = h.Path, title = h.Title, snippet = h.Snippet }));
    }

    public async Task<string> ReadPageAsync([Description("Root-relative page path, for example \"Lore/Necklace.md\"")] string path, CancellationToken ct = default)
    {
        if (!TryPage(path, out var page, out var error)) return Error(error);
        try
        {
            var content = await Workspace.ReadPageAsync(page.Value, ct);
            var info = await Workspace.Graph.GetPageInfoAsync(page.Value, ct);
            var truncated = content.Markdown.Length > maxToolResultChars;
            var markdown = truncated ? content.Markdown[..maxToolResultChars] : content.Markdown;
            return Json(new { path = content.Path, title = info?.Title ?? MarkdownPageParser.DefaultTitle(content.Path), markdown, truncated });
        }
        catch (KnowledgeNotFoundException)
        {
            var resolved = await Workspace.Graph.ResolveAsync("", KnowledgePath.StripExtension(page.Value), ct);
            return resolved is null || string.Equals(resolved, page.Value, StringComparison.OrdinalIgnoreCase)
                ? Error($"No page at {page.Value}")
                : await ReadPageAsync(resolved, ct);
        }
    }

    public async Task<string> ListFolderAsync([Description("Root-relative folder path; \"\" is the root")] string path = "", CancellationToken ct = default)
    {
        if (!KnowledgePath.TryParse(path?.Trim().Trim('/'), isPage: false, out var folder, out var error)) return Error(error);
        try
        {
            var nodes = await Workspace.GetChildrenAsync(folder.Value, ct);
            return Json(nodes.Select(n => new { name = n.Name, path = n.Path, isFolder = n.IsFolder }));
        }
        catch (KnowledgeNotFoundException) { return Error($"No folder at {folder.Value}"); }
    }

    public async Task<string> GetLinksAsync([Description("Root-relative page path")] string path, CancellationToken ct = default)
    {
        if (!TryPage(path, out var page, out var error)) return Error(error);
        var links = await Workspace.Graph.GetLinksAsync(page.Value, ct);
        return Json(new
        {
            outgoing = links.Outgoing.Select(l => new { target = l.RawTarget, resolved = l.ResolvedPath, line = l.Line }),
            backlinks = links.Backlinks.Select(l => new { source = l.SourcePath, line = l.Line }),
        });
    }

    // ── Page edits for Story Chat (D14 as amended, §5.8) ─────────────
    // The chat can create, rewrite and delete any page, but only as a proposal: Plan checks it now, and Apply writes it
    // after the user clicks Apply (SEC-001). Apply passes the page's ticks from Plan, so a newer save is never overwritten.

    public const string CreatePageTool = Prefix + "create_page";
    public const string UpdatePageTool = Prefix + "update_page";
    public const string DeletePageTool = Prefix + "delete_page";

    /// <summary>The kb_ tools that propose a change. Story Chat offers them; paragraph writing never does.</summary>
    public static readonly IReadOnlySet<string> EditToolNames = new HashSet<string>(StringComparer.Ordinal) { CreatePageTool, UpdatePageTool, DeletePageTool };

    public static string ToolName(KbEditKind kind) => kind switch
    {
        KbEditKind.Create => CreatePageTool,
        KbEditKind.Update => UpdatePageTool,
        _ => DeletePageTool,
    };

    /// <summary>Checks a proposed edit and describes it. <see cref="KbEditPlan.Preview"/> has an error when it can't be applied.</summary>
    public async Task<KbEditPlan> PlanAsync(KbEditKind kind, string? path, string? markdown, CancellationToken ct = default)
    {
        var tool = ToolName(kind);
        MutationResult Fail(string message) => new() { Operation = tool, Entity = path ?? "", Error = message };

        if (!TryPage(path, out var page, out var error)) return new KbEditPlan(Fail(error), "", 0);
        var pagePath = page.Value;
        var exists = await Workspace.ExistsAsync(pagePath, ct);

        if (kind == KbEditKind.Create && exists)
            return new KbEditPlan(Fail($"{pagePath} already exists. Read it, then use {UpdatePageTool}."), pagePath, 0);
        if (kind != KbEditKind.Create && !exists)
            return new KbEditPlan(Fail($"No page at {pagePath}"), pagePath, 0);
        if (kind != KbEditKind.Delete)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return new KbEditPlan(Fail("The page's Markdown is required."), pagePath, 0);
            if (Encoding.UTF8.GetByteCount(markdown) > MaxPageBytes)
                return new KbEditPlan(Fail("The page is over the 1 MB limit."), pagePath, 0);
        }

        var ticks = 0L;
        if (exists)
        {
            var current = await Workspace.ReadPageAsync(pagePath, ct);
            ticks = current.ModifiedTicks;
            if (kind == KbEditKind.Update && current.Markdown == markdown)
                return new KbEditPlan(Fail($"The new text of {pagePath} is the same as the current text."), pagePath, ticks);
        }

        var summary = kind switch
        {
            KbEditKind.Create => $"Create the knowledge base page {pagePath}",
            KbEditKind.Update => $"Replace the text of the knowledge base page {pagePath}",
            _ => $"Delete the knowledge base page {pagePath}",
        };
        var preview = new MutationResult
        {
            Success = true, IsPreview = true, Operation = tool, Entity = pagePath, Summary = summary,
            AffectedFiles = [$"{StoryKnowledgeBaseProvider.FolderName}/{pagePath}"],
        };
        return new KbEditPlan(preview, pagePath, ticks);
    }

    /// <summary>Writes an edit that <see cref="PlanAsync"/> accepted. Fails, and writes nothing, when the page changed since.</summary>
    public async Task<MutationResult> ApplyAsync(KbEditKind kind, KbEditPlan plan, string? markdown, CancellationToken ct = default)
    {
        var result = new MutationResult { Operation = ToolName(kind), Entity = plan.PagePath, Summary = plan.Preview.Summary, AffectedFiles = plan.Preview.AffectedFiles };
        try
        {
            if (kind == KbEditKind.Delete)
            {
                var current = await Workspace.ReadPageAsync(plan.PagePath, ct);
                if (current.ModifiedTicks != plan.ModifiedTicks)
                {
                    result.Error = $"{plan.PagePath} changed since the change was proposed.";
                    return result;
                }
                await Workspace.DeleteAsync(plan.PagePath, ct);
            }
            else
            {
                var save = await Workspace.SavePageAsync(plan.PagePath, markdown ?? "", kind == KbEditKind.Create ? 0 : plan.ModifiedTicks, ct);
                if (save.Status is SaveStatus.Conflict or SaveStatus.AlreadyExists)
                {
                    result.Error = save.Status == SaveStatus.Conflict ? $"{plan.PagePath} changed since the change was proposed." : save.Message ?? $"{plan.PagePath} already exists.";
                    return result;
                }
            }
            result.Success = true;
        }
        catch (KnowledgeNotFoundException) { result.Error = $"No page at {plan.PagePath}"; }
        catch (KnowledgeException ex) { result.Error = ex.Message; }
        return result;
    }

    const int MaxPageBytes = 1_048_576;

    // ── Folder management for Story Chat (Q20) ──────────────────────
    // Same proposal flow as page edits. Rename and move go through the workspace, so links into the folder's pages are
    // rewritten as in the Knowledgebase tab. Apply checks the folder again, and a delete fails if its contents changed.

    public const string CreateFolderTool = Prefix + "create_folder";
    public const string RenameFolderTool = Prefix + "rename_folder";
    public const string MoveFolderTool = Prefix + "move_folder";
    public const string DeleteFolderTool = Prefix + "delete_folder";

    /// <summary>Plan Q21: the tools Story Chat gets even when Search knowledgebase is unchecked.</summary>
    public static readonly IReadOnlySet<string> DeleteToolNames = new HashSet<string>(StringComparer.Ordinal) { DeletePageTool, DeleteFolderTool };

    public static readonly IReadOnlySet<string> FolderToolNames =
        new HashSet<string>(StringComparer.Ordinal) { CreateFolderTool, RenameFolderTool, MoveFolderTool, DeleteFolderTool };

    public static string ToolName(KbFolderEditKind kind) => kind switch
    {
        KbFolderEditKind.Create => CreateFolderTool,
        KbFolderEditKind.Rename => RenameFolderTool,
        KbFolderEditKind.Move => MoveFolderTool,
        _ => DeleteFolderTool,
    };

    async Task<bool> IsFolderAsync(KnowledgePath folder, CancellationToken ct) =>
        folder.IsRoot || (!KnowledgePath.IsPagePath(folder.Value) && await Workspace.ExistsAsync(folder.Value, ct));

    /// <summary>
    /// Checks a proposed folder change. <paramref name="argument"/> is the new name (rename) or the new parent folder,
    /// "" for the root (move); create and delete ignore it.
    /// </summary>
    public async Task<KbFolderPlan> PlanFolderAsync(KbFolderEditKind kind, string? path, string? argument, CancellationToken ct = default)
    {
        var tool = ToolName(kind);
        KbFolderPlan Fail(string message) => new(new MutationResult { Operation = tool, Entity = path ?? "", Error = message }, "", null);

        if (!KnowledgePath.TryParse(path?.Trim().Trim('/'), isPage: false, out var folder, out var error)) return Fail(error);
        if (folder.IsRoot) return Fail("A folder path is required; the root can't be changed.");
        if (KnowledgePath.IsPagePath(folder.Value)) return Fail($"{folder.Value} is a page, not a folder.");

        var exists = await Workspace.ExistsAsync(folder.Value, ct);
        if (kind == KbFolderEditKind.Create && exists) return Fail($"{folder.Value} already exists.");
        if (kind != KbFolderEditKind.Create && !exists) return Fail($"No folder at {folder.Value}");

        var root = StoryKnowledgeBaseProvider.FolderName;
        string summary;
        List<string> files = [$"{root}/{folder.Value}/"];
        FolderStats? stats = null;
        switch (kind)
        {
            case KbFolderEditKind.Create:
                summary = $"Create the knowledge base folder {folder.Value}";
                break;
            case KbFolderEditKind.Rename:
            {
                var name = (argument ?? "").Trim();
                if (name.Contains('/') || name.Contains('\\')) return Fail("A folder name can't contain '/' or '\\'.");
                var segError = KnowledgePath.ValidateSegment(name);
                if (segError is not null) return Fail(segError);
                var target = folder.Parent.Combine(name);
                if (string.Equals(target.Value, folder.Value, StringComparison.Ordinal)) return Fail($"{folder.Value} already has that name.");
                if (!string.Equals(target.Value, folder.Value, StringComparison.OrdinalIgnoreCase) && await Workspace.ExistsAsync(target.Value, ct))
                    return Fail($"{target.Value} already exists.");
                summary = $"Rename the knowledge base folder {folder.Value} to {target.Value}";
                files.Add($"{root}/{target.Value}/");
                break;
            }
            case KbFolderEditKind.Move:
            {
                if (!KnowledgePath.TryParse(argument?.Trim().Trim('/'), isPage: false, out var parent, out var parentError)) return Fail(parentError);
                if (!await IsFolderAsync(parent, ct)) return Fail($"No folder at {parent.Value}");
                if (parent == folder || parent.IsUnder(folder)) return Fail("A folder can't be moved into itself or one of its subfolders.");
                if (parent == folder.Parent) return Fail($"{folder.Value} is already in that folder.");
                var target = parent.Combine(folder.Name);
                if (await Workspace.ExistsAsync(target.Value, ct)) return Fail($"{target.Value} already exists.");
                summary = $"Move the knowledge base folder {folder.Value} to {(parent.IsRoot ? "the root" : parent.Value)}";
                files.Add($"{root}/{target.Value}/");
                break;
            }
            default:
                stats = await Workspace.GetFolderStatsAsync(folder.Value, ct);
                summary = $"Delete the knowledge base folder {folder.Value}, with its {Count(stats.Pages, "page")} and {Count(stats.Folders, "subfolder")}";
                break;
        }

        static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

        var preview = new MutationResult { Success = true, IsPreview = true, Operation = tool, Entity = folder.Value, Summary = summary, AffectedFiles = files };
        return new KbFolderPlan(preview, folder.Value, stats);
    }

    /// <summary>Runs a folder change that <see cref="PlanFolderAsync"/> accepted, after checking it again.</summary>
    public async Task<MutationResult> ApplyFolderAsync(KbFolderEditKind kind, KbFolderPlan plan, string? argument, CancellationToken ct = default)
    {
        var result = new MutationResult { Operation = ToolName(kind), Entity = plan.FolderPath, Summary = plan.Preview.Summary, AffectedFiles = plan.Preview.AffectedFiles };
        try
        {
            var check = await PlanFolderAsync(kind, plan.FolderPath, argument, ct);
            if (!check.Preview.Success)
            {
                result.Error = check.Preview.Error;
                return result;
            }
            switch (kind)
            {
                case KbFolderEditKind.Create:
                    await Workspace.CreateFolderAsync(plan.FolderPath, ct);
                    break;
                case KbFolderEditKind.Rename:
                case KbFolderEditKind.Move:
                {
                    var moved = kind == KbFolderEditKind.Rename
                        ? await Workspace.RenameAsync(plan.FolderPath, argument!.Trim(), ct)
                        : await Workspace.MoveAsync(plan.FolderPath, argument?.Trim().Trim('/') ?? "", ct);
                    result.Summary += $". Links updated in {moved.PagesChanged} pages";
                    if (moved.FailedPages.Count > 0) result.Summary += $"; links could not be updated in {string.Join(", ", moved.FailedPages)}";
                    break;
                }
                default:
                    if (check.Stats != plan.Stats)
                    {
                        result.Error = $"{plan.FolderPath} changed since the change was proposed.";
                        return result;
                    }
                    await Workspace.DeleteAsync(plan.FolderPath, ct);
                    break;
            }
            result.Success = true;
        }
        catch (KnowledgeNotFoundException) { result.Error = $"No folder at {plan.FolderPath}"; }
        catch (KnowledgeException ex) { result.Error = ex.Message; }
        return result;
    }
}

public enum KbFolderEditKind { Create, Rename, Move, Delete }

/// <summary>A checked folder change: what the user sees, the folder, and (delete only) its contents when it was checked.</summary>
public sealed record KbFolderPlan(MutationResult Preview, string FolderPath, FolderStats? Stats);

public enum KbEditKind { Create, Update, Delete }

/// <summary>A checked page edit: what the user sees, the page it targets, and the page's ticks when it was checked (0 = new).</summary>
public sealed record KbEditPlan(MutationResult Preview, string PagePath, long ModifiedTicks);
